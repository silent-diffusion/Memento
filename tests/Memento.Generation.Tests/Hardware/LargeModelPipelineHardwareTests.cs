using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Memento.AI;
using Memento.AI.Local;
using Memento.AI.Payload;
using Memento.Core.Engines;
using Memento.Core.Workers;
using Memento.Documents.Model.Modules;
using Memento.Documents.Templates;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Memento.Generation.Tests.Hardware;

/// <summary>
/// The larger models through the real worker, placed as the app places them (ENGINE-NOTES.md §T): the memory planner reads
/// the card's free memory and the RAM available now, the worker loads that placement (experts in RAM for a mixture of
/// experts, a split by blocks for a dense model) and reports what it took; then Meeting minutes of the synthetic meeting
/// and the fixed verification set, as for Qwen3.5 4B. The report (plan, load, speed, recall, precision, verifier, time)
/// is written to <c>large-model-report-{id}.json</c> in the test output folder. Skipped unless the model is installed
/// under the models root (<c>MEMENTO_MODELS_ROOT</c>).
/// </summary>
public sealed class LargeModelPipelineHardwareTests(ITestOutputHelper output)
{
    [WorkerFact("Qwen3.5-35B-A3B-Q4_K_M.gguf")]
    [Trait("Category", "Hardware")]
    public Task TheMixtureOfExpertsWithItsExpertsInRamWritesGroundedMinutes() => RunAsync("qwen3.5-35b-a3b-q4");

    [WorkerFact("Qwen3.5-27B-Q4_K_M.gguf")]
    [Trait("Category", "Hardware")]
    public Task TheDenseModelSplitBetweenCardAndRamWritesGroundedMinutes() => RunAsync("qwen3.5-27b-q4");

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    private async Task RunAsync(string modelId)
    {
        using var launcher = new ProcessWorkerLauncher(new WorkerLocation(WorkerBuild.Executable!), NullLogger<ProcessWorkerLauncher>.Instance);
        using var workers = new WorkerClient(launcher, NullLogger<WorkerClient>.Instance);
        var entry = LocalModelCatalog.Find(modelId)!;
        var path = Path.Combine(WorkerBuild.ModelsRoot, "llama", entry.FileName);
        var snapshot = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance).Sample();

        // MEMENTO_FORCE_PLACEMENT=ram|split runs another placement than the planner's (for the spike's comparisons).
        var plan = MemoryPlanner.Plan(entry.Llm, MemoryPlanInput.From(snapshot, MemoryDemands.None));
        if (Environment.GetEnvironmentVariable("MEMENTO_FORCE_PLACEMENT") == "ram")
        {
            plan = MemoryPlanner.Plan(entry.Llm, MemoryPlanInput.From(snapshot, MemoryDemands.None) with { FreeVramBytes = null });
        }

        output.WriteLine($"Plan: {plan.Reason}; {(plan.Fits ? MemoryPlanWording.Line(plan) : MemoryPlanWording.Refusal(entry.Name, plan))}");
        Assert.True(plan.Fits, $"{entry.Name} does not fit now: {plan.Reason}");
        var provider = new LocalAiProvider(entry, path, new WorkerLocalLlmJobClient(workers), EstimatingTokenCounter.Generic, () => snapshot.DiscreteGpu?.FreeVramBytes, ProviderRegistry.LargerOptions(plan, MemoryDemands.None));
        var material = SyntheticMeeting.Material();
        var payload = PayloadComposer.Compose(material.ToPayloadInputs(null), SyntheticMeeting.AllInputs);
        var facts = new GenerationFacts(material.Details.Title, null, DataModuleComposer.Participants(material), material.Details.Agenda.Items, []);
        var chunkTokens = entry.Llm.ChunkTokens * plan.ContextTokens / entry.Llm.ContextTokens;
        var input = new PipelineInput(BuiltInTemplates.MeetingMinutes, material, payload, SyntheticMeeting.AllInputs, provider, facts, chunkTokens, Math.Min(1400, plan.ContextTokens / 4), Bounded: true, VerifyBatch: GenerationPipeline.LocalVerifyBatchSize);

        var ram = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance);
        var lowest = snapshot.AvailableRamBytes;
        using var watch = new Timer(
            _ =>
            {
                var now = ram.Sample().AvailableRamBytes;
                if (now > 0 && now < Interlocked.Read(ref lowest))
                {
                    Interlocked.Exchange(ref lowest, now);
                }
            },
            null,
            0,
            1000);
        var clock = Stopwatch.StartNew();
        var outcome = await new GenerationPipeline(ModuleCatalog.Default).RunAsync(input, null, CancellationToken.None);
        var pipelineSeconds = clock.Elapsed.TotalSeconds;
        var measured = provider.Measurement();
        var device = provider.LoadedDevice;

        var kept = outcome.Claims.Where(c => c.Kept && c.ModuleId is "m06" or "m07").ToList();
        var truths = SyntheticMeeting.Truths.Where(t => t.Kind != "deferred").ToList();
        var found = truths.Where(t => kept.Any(c => (t.Kind == "decision") == (c.ModuleId == "m06") && t.Matches(c.Text + " " + c.Quote))).Select(t => t.Id).ToList();
        var correct = kept.Count(c => truths.Any(t => (t.Kind == "decision") == (c.ModuleId == "m06") && t.Matches(c.Text + " " + c.Quote)));
        var deferredAsDecision = kept.Count(c => c.ModuleId == "m06" && SyntheticMeeting.Truths.Any(t => t.Kind == "deferred" && t.Matches(c.Text)) && !truths.Any(t => t.Kind == "decision" && t.Matches(c.Text + " " + c.Quote)));
        var verification = await FixedVerification.RunAsync(provider, payload, pad: 1, batch: GenerationPipeline.LocalVerifyBatchSize);
        watch.Change(Timeout.Infinite, Timeout.Infinite);

        var report = new
        {
            model = entry.Name,
            plan = new { plan.Placement, plan.GpuLayers, plan.CardBlocks, plan.CpuExpertLayers, plan.ContextTokens, plan.Threads, plan.VramBytes, plan.RamBytes, plan.TokensPerSecond, line = MemoryPlanWording.Line(plan) },
            loaded = device,
            measured,
            loadedLine = measured is null ? null : MemoryPlanWording.Loaded(entry.Llm, measured),
            freeVramBefore = snapshot.DiscreteGpu?.FreeVramBytes,
            availableRamBefore = snapshot.AvailableRamBytes,
            lowestAvailableRam = Interlocked.Read(ref lowest),
            chunks = outcome.Chunks,
            requests = outcome.Requests.Count,
            pipelineSeconds = Math.Round(pipelineSeconds, 1),
            timings = outcome.Timings,
            recall = Math.Round(found.Count / (double)truths.Count, 3),
            precision = kept.Count == 0 ? 0 : Math.Round(correct / (double)kept.Count, 3),
            missed = truths.Select(t => t.Id).Except(found).ToList(),
            deferredAsDecision,
            fixedVerification = verification.ToString(),
            fixedVerificationSeconds = verification.Seconds,
            warnings = outcome.Warnings,
        };
        var json = JsonSerializer.Serialize(report, ReportJson);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, string.Create(CultureInfo.InvariantCulture, $"large-model-report-{modelId}.json")), json);
        output.WriteLine(json);

        Assert.NotNull(device);
        Assert.Equal(plan.GpuLayers > 0 ? "vulkan" : "cpu", device!.Backend);
        Assert.True(device.GpuLayers <= plan.GpuLayers, "The worker never puts more on the card than planned.");
        Assert.Equal(BuiltInTemplates.MeetingMinutes.Modules().Count(), outcome.Rows.Sum(r => r.Modules.Count));
        // At least the small model's quality (ENGINE-NOTES.md §I.2), with one claim of slack, never on false claims.
        Assert.True(found.Count >= 12, $"Only {found.Count} of {truths.Count} decisions and actions were found.");
        Assert.True(kept.Count == 0 || correct / (double)kept.Count >= 0.95, $"{kept.Count - correct} of {kept.Count} kept decisions and actions are not in the ground truth.");
        Assert.Equal(0, deferredAsDecision);
        Assert.Equal(6, verification.PlantedCaught);
    }
}
