using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Memento.AI.Http;
using Memento.AI.Local;
using Memento.AI.Tests.Fakes;
using Xunit.Abstractions;

namespace Memento.AI.Tests.Local;

/// <summary>
/// Real-model checks on this PC: a grammar-constrained extraction on the processor and on Vulkan (valid JSON,
/// speeds, load time, video memory), cancellation within one second during prompt reading and during generation,
/// video memory released on unload, exact counts against the estimate, and, when a model too big for the card is
/// supplied, the spill watch stopping it.
/// </summary>
[Trait("Category", "Hardware")]
[Collection(LocalLlmHardwareGroup.Name)]
public sealed class LocalLlmHardwareTests(ITestOutputHelper output)
{
    private const string MapSystem =
        "You extract decisions and action items from an excerpt of a meeting transcript. Each line looks like [id] Speaker: words. " +
        "A decision is something the group explicitly agreed in this excerpt; proposals and anything postponed or parked are not decisions. " +
        "An action item is a task someone will do after the meeting; owner and due only if stated, otherwise null. " +
        "Every item cites the line id where it is stated and quotes a few words from that line. Answer with JSON only.";

    private const string Excerpt = """
        [1] Speaker A: Okay, let's settle the release date first.
        [2] Speaker B: I'd propose Thursday the twelfth, if QA is done by then.
        [3] Speaker A: Agreed, we ship release 3.2 on Thursday, November 12.
        [4] Speaker C: I'll update the help-center article on offline mode by Friday.
        [5] Speaker B: Someone should also tell the sales team about the annual plan change.
        [6] Speaker A: The price increase we park until the pricing review.
        """;

    private static readonly JsonElement MapSchema = JsonDocument.Parse("""
        {"type":"object","properties":{
          "decisions":{"type":"array","items":{"type":"object","properties":{
            "decision":{"type":"string"},"line":{"type":"integer"},"quote":{"type":"string"}},
            "required":["decision","line","quote"],"additionalProperties":false}},
          "action_items":{"type":"array","items":{"type":"object","properties":{
            "task":{"type":"string"},"owner":{"type":["string","null"]},"due":{"type":["string","null"]},"line":{"type":"integer"}},
            "required":["task","owner","due","line"],"additionalProperties":false}}
        },"required":["decisions","action_items"],"additionalProperties":false}
        """).RootElement.Clone();

    [LlmHardwareFact(LlmHardware.Ministral3B)]
    public Task MinistralExtractsValidJsonOnTheProcessor() => ExtractAsync(LocalLlmDevices.Cpu);

    [LlmHardwareFact(LlmHardware.Ministral3B, needsVulkan: true)]
    public Task MinistralExtractsValidJsonOnVulkan() => ExtractAsync(LocalLlmDevices.Gpu);

    [LlmHardwareFact(LlmHardware.Ministral3B)]
    public Task CancellingDuringPromptReadingStopsWithinOneSecondOnTheProcessor() => CancelAsync(LocalLlmDevices.Cpu, duringGeneration: false);

    [LlmHardwareFact(LlmHardware.Ministral3B)]
    public Task CancellingDuringGenerationStopsWithinOneSecondOnTheProcessor() => CancelAsync(LocalLlmDevices.Cpu, duringGeneration: true);

    [LlmHardwareFact(LlmHardware.Ministral3B, needsVulkan: true)]
    public Task CancellingDuringPromptReadingStopsWithinOneSecondOnVulkan() => CancelAsync(LocalLlmDevices.Gpu, duringGeneration: false);

    [LlmHardwareFact(LlmHardware.Ministral3B, needsVulkan: true)]
    public Task CancellingDuringGenerationStopsWithinOneSecondOnVulkan() => CancelAsync(LocalLlmDevices.Gpu, duringGeneration: true);

    [LlmHardwareFact(LlmHardware.Qwen4B, needsVulkan: true)]
    public async Task QwenExtractsValidJsonOnVulkanWithTheSpikeGrammar()
    {
        LlmHardware.EnsureBackend();
        var job = LlmHardware.Job(LlmHardware.Qwen4B, LocalModelCatalog.Qwen35FourB, LocalLlmDevices.Gpu, [Prompt(LocalGrammars.DecisionsAndActions, 600)], context: 8192);

        var result = await Runner().RunAsync(job, null, CancellationToken.None);

        var answer = Assert.Single(result.Outputs);
        Report("qwen3.5-4b vulkan (spike grammar)", result, answer);
        Assert.Equal(LocalLlmStopReasons.EndOfGeneration, answer.StopReason);
        Assert.NotNull(CloudJson.ParseJsonAnswer(answer.Text));
    }

    [LlmHardwareFact(LlmHardware.Ministral3B)]
    public async Task ExactCountsAndTheEstimateAgree()
    {
        LlmHardware.EnsureBackend();
        var (segments, _, _) = SyntheticTranscript.Create(300);
        var lines = Memento.AI.Payload.PayloadComposer.RenderLines(segments, SyntheticTranscript.Speakers(4)).Select(l => l.Rendered).ToList();
        var job = LlmHardware.Job(LlmHardware.Ministral3B, LocalModelCatalog.Ministral3ThreeB, LocalLlmDevices.Cpu, []) with { TokenizeTexts = [string.Join('\n', lines), Excerpt, "[INST]"], WarmUp = false };

        var result = await Runner().RunAsync(job, null, CancellationToken.None);

        var exact = result.TokenCounts![0];
        var estimate = EstimatingTokenCounter.Generic.Count(string.Join('\n', lines));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"tokens: exact {exact}, generic estimate {estimate} ({estimate / (double)exact:0.00}x), claude estimate {EstimatingTokenCounter.Claude.Count(string.Join('\n', lines))}"));
        Assert.True(estimate >= exact * 0.9, $"estimate {estimate} vs exact {exact}");
        using (var vocabularyOnly = new LlamaTokenCounter(LlmHardware.ModelPath(LlmHardware.Ministral3B)))
        {
            Assert.True(vocabularyOnly.IsExact);
            Assert.Equal(exact, vocabularyOnly.Count(string.Join('\n', lines)));
        }

        Assert.InRange(result.TokenCounts[1], 60, 200);

        // Content is tokenized without special-token parsing: "[INST]" in a transcript is text, not the control token.
        Assert.True(result.TokenCounts[2] > 1, $"[INST] became {result.TokenCounts[2]} token(s)");
    }

    [LlmHardwareFact(LlmHardware.Ministral3B, needsVulkan: true)]
    public async Task UnloadingReleasesTheVideoMemory()
    {
        LlmHardware.EnsureBackend();
        var memory = new PdhGpuProcessMemory();
        var before = memory.Sample();
        var job = LlmHardware.Job(LlmHardware.Ministral3B, LocalModelCatalog.Ministral3ThreeB, LocalLlmDevices.Gpu, [Prompt(null, 8)]);

        var result = await Runner().RunAsync(job, null, CancellationToken.None);
        var after = memory.Sample();

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"dedicated: before {Mb(before?.DedicatedBytes)}, loaded {Mb(result.DedicatedVramBytes)}, after unload {Mb(after?.DedicatedBytes)}"));
        Assert.True(result.DedicatedVramBytes > 2000L << 20, "the model should have been on the graphics card");
        Assert.True(after!.DedicatedBytes - before!.DedicatedBytes < 256L << 20, "video memory should be released after unload");
    }

    /// <summary>
    /// Opt-in: <c>MEMENTO_LLM_SPILL_MODEL</c> names a GGUF too large for the card (the spike used Ministral 8B on 6 GB).
    /// Forcing every layer onto the card must end in <c>ai.notEnoughVram</c> (spill or null context), not a slow run.
    /// </summary>
    [Fact]
    [Trait("Category", "Hardware")]
    public async Task AModelThatDoesNotFitIsStoppedAsNotEnoughVram()
    {
        var path = Environment.GetEnvironmentVariable("MEMENTO_LLM_SPILL_MODEL");
        if (path is null || !File.Exists(path) || !LlmHardware.UseVulkan)
        {
            output.WriteLine("Skipped: set MEMENTO_LLM_SPILL_MODEL to a GGUF larger than the graphics card's memory.");
            return;
        }

        LlmHardware.EnsureBackend();
        var entry = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)!;
        var job = new LocalLlmJob
        {
            ModelPath = path,
            ModelId = "spill-test",
            ModelName = Path.GetFileNameWithoutExtension(path),
            Profile = entry.Llm with { Layers = 40 },
            Device = LocalLlmDevices.Gpu,
            ContextTokens = int.TryParse(Environment.GetEnvironmentVariable("MEMENTO_LLM_SPILL_CONTEXT"), out var context) ? context : 16384,
            GpuLayers = int.TryParse(Environment.GetEnvironmentVariable("MEMENTO_LLM_SPILL_LAYERS"), out var layers) ? layers : 99,
            Prompts = [Prompt(null, 64)],
        };
        var watch = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<LocalLlmException>(async () =>
        {
            var result = await Runner().RunAsync(job, null, CancellationToken.None);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"not stopped: dedicated {Mb(result.DedicatedVramBytes)}, shared growth {Mb(result.SharedVramGrowthBytes)}, {result.Outputs[0].TokensPerSecond:0.0} tok/s"));
        });

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"stopped after {watch.ElapsedMilliseconds} ms: {error.Code} ({error.Error.Diagnostic}) — {error.Message}"));
        Assert.Equal(AiErrorCodes.NotEnoughVram, error.Code);
    }

    private async Task ExtractAsync(string device)
    {
        var backend = LlmHardware.EnsureBackend();
        var probe = new GgmlVramProbe().Read();
        var memory = new PdhGpuProcessMemory().Sample();
        var job = LlmHardware.Job(LlmHardware.Ministral3B, LocalModelCatalog.Ministral3ThreeB, device, [Prompt(JsonSchemaGrammar.FromSchema(MapSchema), 600)]);

        var result = await Runner().RunAsync(job, null, CancellationToken.None);

        var answer = Assert.Single(result.Outputs);
        output.WriteLine($"backend {backend}; free VRAM before {Mb(probe?.FreeBytes)}; process dedicated before {Mb(memory?.DedicatedBytes)}");
        Report("ministral-3-3b " + device, result, answer);
        Assert.Equal(LocalLlmStopReasons.EndOfGeneration, answer.StopReason);
        var json = CloudJson.ParseJsonAnswer(answer.Text);
        Assert.NotNull(json);
        Assert.Equal(device == LocalLlmDevices.Gpu ? "vulkan" : "cpu", result.Device.Backend);
        var decisions = json!.Value.GetProperty("decisions");
        Assert.True(decisions.GetArrayLength() >= 1, answer.Text);
        Assert.All(decisions.EnumerateArray().Concat(json.Value.GetProperty("action_items").EnumerateArray()), item => Assert.InRange(item.GetProperty("line").GetInt32(), 1, 6));
    }

    private async Task CancelAsync(string device, bool duringGeneration)
    {
        LlmHardware.EnsureBackend();

        // A long prompt to cancel while it is being read (several seconds even on Vulkan), a short one to cancel mid-answer.
        var transcript = string.Join('\n', Enumerable.Repeat(Excerpt, duringGeneration ? 2 : 40));
        var prompt = new LocalLlmPrompt("hardware.cancel", MapSystem, [new LocalLlmTurn(LocalLlmTurn.User, transcript + "\n\nSummarise every line in detail.")], 1500);
        var job = LlmHardware.Job(LlmHardware.Ministral3B, LocalModelCatalog.Ministral3ThreeB, device, [prompt], context: 8192);
        var factory = new LlamaLocalLlmEngineFactory(new SpyLogger<LlamaLocalLlmEngineFactory>());
        var engine = await factory.LoadAsync(job, null, CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var cancelledAt = 0L;
        if (duringGeneration)
        {
            var produced = 0;
            var generation = engine.GenerateAsync(0, prompt, _ =>
            {
                if (Interlocked.Increment(ref produced) == 3)
                {
                    Volatile.Write(ref cancelledAt, Stopwatch.GetTimestamp());
                    cancel.Cancel();
                }
            }, cancel.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation);
        }
        else
        {
            var generation = engine.GenerateAsync(0, prompt, null, cancel.Token);
            await Task.Delay(500);
            Volatile.Write(ref cancelledAt, Stopwatch.GetTimestamp());
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation);
        }

        var stoppedIn = Stopwatch.GetElapsedTime(Volatile.Read(ref cancelledAt));
        var unloadStarted = Stopwatch.GetTimestamp();
        engine.Dispose();
        var unload = Stopwatch.GetElapsedTime(unloadStarted);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{device} cancel during {(duringGeneration ? "generation" : "prompt reading")}: stopped {stoppedIn.TotalMilliseconds:0} ms after the request; unload {unload.TotalMilliseconds:0} ms"));
        Assert.True(stoppedIn < TimeSpan.FromSeconds(1), $"stopped after {stoppedIn.TotalMilliseconds} ms");
    }

    private static LocalLlmPrompt Prompt(string? grammar, int maxTokens) =>
        new("hardware.map", MapSystem, [new LocalLlmTurn(LocalLlmTurn.User, Excerpt)], maxTokens, grammar);

    private static LocalLlmJobRunner Runner() =>
        new(new LlamaLocalLlmEngineFactory(new SpyLogger<LlamaLocalLlmEngineFactory>()), new SpyLogger<LocalLlmJobRunner>());

    private void Report(string label, LocalLlmResult result, LocalLlmOutput answer) =>
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: device {result.Device.Backend} {result.Device.GpuName} layers {result.Device.GpuLayers} ctx {result.Device.ContextTokens} threads {result.Device.Threads}; " +
            $"load {result.LoadMs:0} ms, warm-up {result.WarmUpMs:0} ms; prompt {answer.PromptTokens} tok at {answer.PromptTokensPerSecond:0.0} tok/s; " +
            $"output {answer.OutputTokens} tok at {answer.TokensPerSecond:0.0} tok/s; dedicated VRAM {Mb(result.DedicatedVramBytes)}, shared growth {Mb(result.SharedVramGrowthBytes)}\n{answer.Text}"));

    private static string Mb(long? bytes) => bytes is { } b ? string.Create(CultureInfo.InvariantCulture, $"{b / (1024 * 1024)} MB") : "n/a";
}
