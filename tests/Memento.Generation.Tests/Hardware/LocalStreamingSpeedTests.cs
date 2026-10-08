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
using Memento.Generation.Documents;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Memento.Generation.Tests.Hardware;

/// <summary>
/// How fast the real worker writes while its tokens are streamed to the host (ENGINE-NOTES.md §I.3): the map requests of
/// Meeting minutes over the synthetic meeting's chunks, grammar-constrained as the pipeline sends them, on one model load,
/// with every progress line read the way the app reads it. Reports output tokens per second, the time spent writing and
/// the number of streamed lines, to <c>local-streaming-report.json</c> in the test output folder. Run it before and after
/// a change to the streaming path; <c>MEMENTO_STREAM_ROUNDS</c> repeats the requests (default 2).
/// </summary>
public sealed class LocalStreamingSpeedTests(ITestOutputHelper output)
{
    private const string QwenFile = "Qwen3.5-4B-Q4_K_M.gguf";

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    [WorkerFact(QwenFile)]
    [Trait("Category", "Hardware")]
    public async Task StreamingTheTokensDoesNotSlowTheLocalModel()
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
        var chunkTokens = entry.Llm.ChunkTokens * plan.ContextTokens / entry.Llm.ContextTokens;
        var input = new PipelineInput(BuiltInTemplates.MeetingMinutes, material, payload, SyntheticMeeting.AllInputs, provider, facts, chunkTokens, Math.Min(1400, plan.ContextTokens / 4), Bounded: true, VerifyBatch: GenerationPipeline.LocalVerifyBatchSize);
        var chunks = TranscriptChunker.Chunk(payload.TranscriptLines, new ChunkOptions(chunkTokens, new ProviderTokenCounter(provider)), material.ToPayloadInputs(null).Chapters);
        var requests = GenerationPipeline.Tasks(input)
            .SelectMany(t => chunks.Select(c => MapPrompts.Build(t, payload, c, chunks.Count, ModuleCatalog.Default, input.MapOutputTokens, bounded: true)))
            .ToList();
        var rounds = int.TryParse(Environment.GetEnvironmentVariable("MEMENTO_STREAM_ROUNDS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 2;

        // MEMENTO_STREAM_LIVE=1: the requests go through the pipeline's RequestRunner with a Live output feed, as the app
        // sends them (the feed's events are counted, not shown); otherwise the session's progress lines are only counted.
        var live = Environment.GetEnvironmentVariable("MEMENTO_STREAM_LIVE") == "1";
        var lines = 0;
        long streamedChars = 0;
        var events = 0;
        var feed = live ? new GenerationOutputFeed("g1", e =>
        {
            Interlocked.Increment(ref events);
            if (e.Kind == GenerationOutputFeed.TokenKind)
            {
                Interlocked.Add(ref streamedChars, e.Text.Length);
            }
        }, TimeProvider.System) : null;
        var relay = new InlineProgress<AiProgress>(p =>
        {
            Interlocked.Increment(ref lines);
            if (p.Delta is { } delta)
            {
                Interlocked.Add(ref streamedChars, delta.Length);
            }
        });
        await using var runner = new RequestRunner(provider, output: feed);
        await using var session = live ? null : await provider.OpenSessionAsync(CancellationToken.None);
        AiRequest[] warmUp = [AiRequest.Create("warmup", string.Empty, "Say hello.", 8)];
        _ = session is null ? await runner.RunAsync(warmUp, null, CancellationToken.None) : await session.GenerateManyAsync(warmUp, null, CancellationToken.None);
        var perRound = new List<object>();
        double totalGenerateMs = 0;
        long totalOutput = 0;
        long totalChars = 0;
        double wallMs = 0;
        for (var round = 0; round < rounds; round++)
        {
            lines = 0;
            events = 0;
            streamedChars = 0;
            var clock = Stopwatch.StartNew();
            var responses = session is null
                ? await runner.RunAsync(requests, null, CancellationToken.None)
                : await session.GenerateManyAsync(requests, relay, CancellationToken.None);
            clock.Stop();
            var generateMs = responses.Sum(x => x.Timings.Generation?.TotalMilliseconds ?? 0);
            var promptMs = responses.Sum(x => x.Timings.PromptEvaluation?.TotalMilliseconds ?? 0);
            var outputTokens = responses.Sum(x => x.Usage.OutputTokens);
            var chars = responses.Sum(x => x.Text.Length);
            totalGenerateMs += generateMs;
            totalOutput += outputTokens;
            totalChars += chars;
            wallMs += clock.Elapsed.TotalMilliseconds;
            perRound.Add(new
            {
                requests = responses.Count,
                outputTokens,
                tokensPerSecond = Math.Round(outputTokens / (generateMs / 1000), 2),
                writingSeconds = Math.Round(generateMs / 1000, 2),
                readingSeconds = Math.Round(promptMs / 1000, 2),
                wallSeconds = Math.Round(clock.Elapsed.TotalSeconds, 2),
                progressLines = lines,
                liveOutputEvents = events,
                streamedChars,
                answerChars = chars,
            });
        }

        if (feed is not null)
        {
            await feed.CompleteAsync();
        }

        var report = new
        {
            model = entry.Name,
            device,
            live,
            context = plan.ContextTokens,
            chunks = chunks.Count,
            rounds,
            outputTokens = totalOutput,
            tokensPerSecond = Math.Round(totalOutput / (totalGenerateMs / 1000), 2),
            wallSeconds = Math.Round(wallMs / 1000, 2),
            perRound,
        };
        var json = JsonSerializer.Serialize(report, ReportJson);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "local-streaming-report.json"), json);
        output.WriteLine(json);

        Assert.True(totalOutput > 0);
        Assert.True(totalChars > 0);
    }
}
