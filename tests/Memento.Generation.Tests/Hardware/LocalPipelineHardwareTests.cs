using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Memento.AI;
using Memento.AI.Local;
using Memento.AI.Payload;
using Memento.Core.Engines;
using Memento.Core.Workers;
using Memento.Documents.Model.Modules;
using Memento.Documents.Templates;
using Memento.Generation.Documents;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Memento.Generation.Tests.Hardware;

/// <summary>
/// The real local provider: Memento.Worker.exe with llama.cpp and a real GGUF model, through Core's WorkerClient, as the
/// app runs it. The pipeline test generates Meeting minutes for the synthetic meeting with Qwen3.5 4B on the graphics card
/// and reports recall, precision, the verifier's accuracy on the spike's 20 fixed claims (14 true, 6 planted false) and
/// the wall-clock time, also to <c>local-pipeline-report.json</c> in the test output folder.
/// </summary>
public sealed class LocalPipelineHardwareTests(ITestOutputHelper output)
{
    private const string QwenFile = "Qwen3.5-4B-Q4_K_M.gguf";

    [WorkerFact]
    public async Task AMissingModelFileIsReportedByTheRealWorker()
    {
        using var launcher = new ProcessWorkerLauncher(new WorkerLocation(WorkerBuild.Executable!), NullLogger<ProcessWorkerLauncher>.Instance);
        using var workers = new WorkerClient(launcher, NullLogger<WorkerClient>.Instance);
        var entry = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)! with { SizeBytes = 0 };
        var missing = Path.Combine(Path.GetTempPath(), "memento-no-such-model.gguf");
        var provider = new LocalAiProvider(entry, missing, new WorkerLocalLlmJobClient(workers), EstimatingTokenCounter.Generic, () => null, new LocalAiOptions { Device = LocalLlmDevices.Cpu });

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("map.test", "s", "u", 16), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ModelNotInstalled, error.Code);
        Assert.Contains("Ministral 3 3B", error.Message, StringComparison.Ordinal);
    }

    [WorkerFact(QwenFile)]
    [Trait("Category", "Hardware")]
    public async Task QwenOnTheGraphicsCardWritesGroundedMinutesOfTheSyntheticMeeting()
    {
        using var launcher = new ProcessWorkerLauncher(new WorkerLocation(WorkerBuild.Executable!), NullLogger<ProcessWorkerLauncher>.Instance);
        using var workers = new WorkerClient(launcher, NullLogger<WorkerClient>.Instance);
        var entry = LocalModelCatalog.Find(LocalModelCatalog.Qwen35FourB)!;
        var path = Path.Combine(WorkerBuild.ModelsRoot, "llama", QwenFile);
        var free = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance).Sample().DiscreteGpu?.FreeVramBytes;
        // MEMENTO_CONTEXT_TOKENS asks for a context size (the planner may still give less).
        var askedContext = int.TryParse(Environment.GetEnvironmentVariable("MEMENTO_CONTEXT_TOKENS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked) ? asked : 0;
        var plan = LocalVramPlanner.Plan(entry.Llm, LocalLlmDevices.Auto, free, askedContext, Ai.ProviderRegistry.VramMarginBytes);
        var device = plan.UseGpu ? LocalLlmDevices.Gpu : LocalLlmDevices.Cpu;
        var provider = new LocalAiProvider(entry, path, new WorkerLocalLlmJobClient(workers), EstimatingTokenCounter.Generic, () => free, new LocalAiOptions { Device = device, ContextTokens = plan.ContextTokens });
        var material = SyntheticMeeting.Material();
        var payload = PayloadComposer.Compose(material.ToPayloadInputs(null), SyntheticMeeting.AllInputs);
        var facts = new GenerationFacts(material.Details.Title, null, DataModuleComposer.Participants(material), material.Details.Agenda.Items, []);
        // MEMENTO_CHUNK_TOKENS overrides the profile's budget; MEMENTO_PIPELINE_DUMP names a file (outside the repository)
        // that receives every request's end and its answer.
        var chunkTokens = int.TryParse(Environment.GetEnvironmentVariable("MEMENTO_CHUNK_TOKENS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var forced)
            ? forced
            : entry.Llm.ChunkTokens * plan.ContextTokens / entry.Llm.ContextTokens;
        // MEMENTO_VERIFY_BATCH overrides the verification batch size (1: one question per request).
        var verifyBatch = int.TryParse(Environment.GetEnvironmentVariable("MEMENTO_VERIFY_BATCH"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var batch) ? batch : GenerationPipeline.LocalVerifyBatchSize;
        var dump = Environment.GetEnvironmentVariable("MEMENTO_PIPELINE_DUMP");
        var log = new StringBuilder();
        var stats = new Dictionary<string, (int Count, long Prompt, long Output, double PromptMs, double GenerateMs)>(StringComparer.Ordinal);
        var input = new PipelineInput(BuiltInTemplates.MeetingMinutes, material, payload, SyntheticMeeting.AllInputs, provider, facts, chunkTokens, Math.Min(1400, plan.ContextTokens / 4), Bounded: true, VerifyBatch: verifyBatch)
        {
            OnResponse = (request, response) =>
            {
                var user = request.Messages[^1].Content;
                lock (log)
                {
                    var key = request.Purpose.Split('#')[0];
                    var s = stats.GetValueOrDefault(key);
                    stats[key] = (s.Count + 1, s.Prompt + response.Usage.InputTokens, s.Output + response.Usage.OutputTokens, s.PromptMs + (response.Timings.PromptEvaluation?.TotalMilliseconds ?? 0), s.GenerateMs + (response.Timings.Generation?.TotalMilliseconds ?? 0));
                    log.Append("===== ").Append(request.Purpose).Append(" | ").Append(response.ProviderStopReason).Append('\n')
                        .Append(user[^Math.Min(700, user.Length)..]).Append("\n--- answer ---\n").Append(response.Text).Append("\n\n");
                }
            },
        };

        var clock = Stopwatch.StartNew();
        var outcome = await new GenerationPipeline(ModuleCatalog.Default).RunAsync(input, null, CancellationToken.None);
        var pipelineSeconds = clock.Elapsed.TotalSeconds;
        if (dump is not null)
        {
            await File.WriteAllTextAsync(dump, log.ToString());
        }

        var kept = outcome.Claims.Where(c => c.Kept && c.ModuleId is "m06" or "m07").ToList();
        var truths = SyntheticMeeting.Truths.Where(t => t.Kind != "deferred").ToList();
        var found = truths.Where(t => kept.Any(c => (t.Kind == "decision") == (c.ModuleId == "m06") && t.Matches(c.Text + " " + c.Quote))).Select(t => t.Id).ToList();
        var correct = kept.Count(c => truths.Any(t => (t.Kind == "decision") == (c.ModuleId == "m06") && t.Matches(c.Text + " " + c.Quote)));
        var deferredAsDecision = kept.Count(c => c.ModuleId == "m06" && SyntheticMeeting.Truths.Any(t => t.Kind == "deferred" && t.Matches(c.Text)) && !truths.Any(t => t.Kind == "decision" && t.Matches(c.Text + " " + c.Quote)));
        var agenda = outcome.Rows.SelectMany(r => r.Modules).Single(m => m.Id == "m04");
        var notReached = AgendaNotReached(agenda);

        // The spike's fixed verification set: each claim with the lines of the truth item it is about and one neighbour
        // either side, asked as the pipeline asks (and batched as it batches).
        var verification = await FixedVerification.RunAsync(provider, payload, pad: 1, batch: verifyBatch);

        var report = new
        {
            model = entry.Name,
            device,
            context = plan.ContextTokens,
            chunks = outcome.Chunks,
            requests = outcome.Requests.Count,
            pipelineSeconds = Math.Round(pipelineSeconds, 1),
            timings = outcome.Timings,
            recall = Math.Round(found.Count / (double)truths.Count, 3),
            precision = kept.Count == 0 ? 0 : Math.Round(correct / (double)kept.Count, 3),
            found,
            missed = truths.Select(t => t.Id).Except(found).ToList(),
            keptDecisionsAndActions = kept.Count,
            deferredAsDecision,
            agendaNotReached = notReached,
            verifyBatch,
            fixedVerification = verification.ToString(),
            fixedVerificationSeconds = verification.Seconds,
            fixedVerificationWrong = verification.Wrong,
            byPurpose = stats.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Key}: {s.Value.Count} requests, {s.Value.Prompt} prompt / {s.Value.Output} output tokens, {s.Value.PromptMs / 1000:0.0} s reading, {s.Value.GenerateMs / 1000:0.0} s writing")),
            modules = outcome.Modules.Where(m => m.Source == "ai").Select(m => new { m.ModuleId, m.Type, m.Claims, m.Verified, m.Dropped, m.NotDiscussed }),
            keptClaims = kept.Select(c => $"{c.ModuleId} [{Memento.Documents.Model.Timecode.Format(c.T ?? 0)}] {c.Text} | owner={c.Owner} due={c.Due}"),
            dropped = outcome.Claims.Where(c => !c.Kept && c.ModuleId is "m06" or "m07").Select(c => $"{c.ModuleId} {c.Text} — {c.Note}"),
            warnings = outcome.Warnings,
        };
        var json = JsonSerializer.Serialize(report, ReportJson);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "local-pipeline-report.json"), json);
        output.WriteLine(json);

        Assert.True(outcome.Chunks >= 1);
        Assert.Equal(BuiltInTemplates.MeetingMinutes.Modules().Count(), outcome.Rows.Sum(r => r.Modules.Count));
        // The M4 integration's numbers (ENGINE-NOTES.md §J): recall 13/14, precision 1.0, the fixed set 20/20, the two
        // undiscussed agenda items and only those not reached. Thresholds leave one claim of slack, never on false claims.
        Assert.True(found.Count >= 13, $"Only {found.Count} of {truths.Count} decisions and actions were found (recall at least 0.9).");
        Assert.True(kept.Count == 0 || correct / (double)kept.Count >= 0.95, $"{kept.Count - correct} of {kept.Count} kept decisions and actions are not in the ground truth.");
        Assert.Equal(0, deferredAsDecision);
        Assert.Equal(6, verification.PlantedCaught);
        Assert.True(verification.Right >= 19, $"The verifier got {verification.Right} of 20 fixed claims right.");
        Assert.Equal(SyntheticMeeting.AgendaNotDiscussed, notReached);
    }

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static List<int> AgendaNotReached(Memento.Documents.Model.ModuleBlock agenda)
    {
        var list = agenda.Blocks.OfType<Memento.Documents.Model.Blocks.ListBlock>().Single();
        return list.Items.Select((item, i) => (Text: string.Concat(item.Runs.Select(r => r.Text)), Number: i + 1))
            .Where(x => x.Text.Contains(ModuleWriter.NotReached, StringComparison.Ordinal))
            .Select(x => x.Number)
            .ToList();
    }
}
