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
        var plan = LocalVramPlanner.Plan(entry.Llm, LocalLlmDevices.Auto, free, 0, Ai.ProviderRegistry.VramMarginBytes);
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
        var dump = Environment.GetEnvironmentVariable("MEMENTO_PIPELINE_DUMP");
        var log = new StringBuilder();
        var input = new PipelineInput(BuiltInTemplates.MeetingMinutes, material, payload, SyntheticMeeting.AllInputs, provider, facts, chunkTokens, Math.Min(1400, plan.ContextTokens / 4), Bounded: true, BatchVerify: false)
        {
            OnResponse = dump is null ? null : (request, response) =>
            {
                var user = request.Messages[^1].Content;
                lock (log)
                {
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

        // The spike's fixed verification set: each claim with the span of the truth item it is about.
        clock.Restart();
        var (verifiedRight, plantedCaught, trueAccepted) = await FixedVerificationAsync(provider, payload);
        var verifySeconds = clock.Elapsed.TotalSeconds;

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
            fixedVerification = $"{verifiedRight}/20 ({trueAccepted}/14 true accepted, {plantedCaught}/6 planted caught)",
            fixedVerificationSeconds = Math.Round(verifySeconds, 1),
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
        Assert.True(found.Count >= 7, $"Only {found.Count} of {truths.Count} decisions and actions were found.");
        Assert.Equal(0, deferredAsDecision);
        Assert.True(verifiedRight >= 14, $"The verifier got {verifiedRight} of 20 fixed claims right.");
    }

    private static async Task<(int Right, int PlantedCaught, int TrueAccepted)> FixedVerificationAsync(LocalAiProvider provider, ComposedPayload payload)
    {
        var claims = new (string Claim, string Of, bool Expected)[]
        {
            ("Decision: Release 3.2 will ship on Thursday, November 12.", "D1", true),
            ("Decision: Recurring invoice templates are moved out of 3.2 into 3.3.", "D2", true),
            ("Decision: Sync conflicts will be handled with last write wins plus a conflict log.", "D3", true),
            ("Decision: The annual-only Business plan is removed from the pricing page and a monthly/annual toggle with monthly as default is shown.", "D4", true),
            ("Decision: The Tallyhouse integration starts with the read-only bank feed, without payment initiation.", "D5", true),
            ("Action item: Cut the 3.3 branch and put the recurring template code behind a feature flag.", "A1", true),
            ("Luis Brandt is the person who will do this task: Cut the 3.3 branch and put the recurring template code behind a feature flag.", "A1", true),
            ("Action item: Deliver final pricing page mockups.", "A2", true),
            ("Mei Tanaka is the person who will do this task: Deliver final pricing page mockups.", "A2", true),
            ("Action item: Send the list of the top twenty offline sync tickets to Luis.", "A3", true),
            ("The deadline stated for this task is \"by Monday\": Write the design doc for the conflict log.", "A4", true),
            ("Action item: Email the Tallyhouse contact to confirm the read-only scope and ask for sandbox credentials.", "A5", true),
            ("Action item: Run five usability sessions on the pricing toggle with existing customers.", "A6", true),
            ("Action item: Update the help-center article on offline mode.", "U1", true),
            ("Luis Brandt is the person who will do this task: Deliver the final pricing page mockups.", "A2", false),
            ("Decision: The team decided to raise the Pro price to 19 dollars.", "F1", false),
            ("Decision: Release 3.2 will ship on November 19.", "D1", false),
            ("Decision: The team decided to start weekend support coverage.", "F2", false),
            ("Sam Whitfield is the person who will do this task: Update the help-center article on offline mode.", "U1", false),
            ("Decision: The first version of the Tallyhouse integration will include payment initiation.", "D5", false),
        };
        var transcript = new TranscriptIndex(payload.TranscriptLines);
        var requests = claims.Select(c =>
        {
            var lines = SyntheticMeeting.LinesOf(c.Of);
            var excerpt = new StringBuilder();
            foreach (var id in transcript.Ids.Where(id => id >= lines[0] && id <= lines[^1]))
            {
                var entry = transcript.Find(id)!;
                excerpt.Append(CultureInfo.InvariantCulture, $"[{id}] {entry.Speaker}: {entry.Text}\n");
            }

            return AiRequest.Create("verify.claim", VerifyPrompts.System, $"Excerpt:\n{excerpt.ToString().TrimEnd()}\n\nClaim: {c.Claim}", 160) with { JsonSchema = VerdictSchema, Temperature = 0 };
        }).ToList();
        var responses = await provider.GenerateManyAsync(requests, null, CancellationToken.None);
        int right = 0, planted = 0, accepted = 0;
        for (var i = 0; i < claims.Length; i++)
        {
            var (supported, _) = responses[i].Json is { } json ? VerifyPrompts.ParseSingle(json) : (null, null);
            if (supported == claims[i].Expected)
            {
                right++;
                planted += claims[i].Expected ? 0 : 1;
                accepted += claims[i].Expected ? 1 : 0;
            }
        }

        return (right, planted, accepted);
    }

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonElement VerdictSchema = JsonDocument.Parse("""
        {"type":"object","properties":{"reason":{"type":"string"},"supported":{"type":"boolean"}},"required":["reason","supported"],"additionalProperties":false}
        """).RootElement.Clone();

    private static List<int> AgendaNotReached(Memento.Documents.Model.ModuleBlock agenda)
    {
        var list = agenda.Blocks.OfType<Memento.Documents.Model.Blocks.ListBlock>().Single();
        return list.Items.Select((item, i) => (Text: string.Concat(item.Runs.Select(r => r.Text)), Number: i + 1))
            .Where(x => x.Text.Contains(ModuleWriter.NotReached, StringComparison.Ordinal))
            .Select(x => x.Number)
            .ToList();
    }
}
