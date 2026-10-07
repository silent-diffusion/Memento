using System.Text.Json;
using Memento.AI.Local;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Local;

/// <summary>The worker-style job runner and the local provider, end to end over the JSON-lines protocol, with a fake engine.</summary>
public sealed class LocalJobRunnerTests : IDisposable
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}
        """).RootElement.Clone();

    private readonly string _folder = Directory.CreateTempSubdirectory("memento-ai-tests-").FullName;
    private readonly FakeLocalEngineFactory _engines = new();
    private readonly LocalModelEntry _model;
    private readonly string _modelPath;

    public LocalJobRunnerTests()
    {
        _modelPath = Path.Combine(_folder, "model.gguf");
        File.WriteAllBytes(_modelPath, [1, 2, 3, 4]);
        _model = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)! with { SizeBytes = 4 };
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task AJsonRequestRoundTripsThroughTheWorkerProtocol()
    {
        await using var worker = new PipeWorker();
        var provider = Provider(worker);
        var progress = new ProgressLog<AiProgress>();
        var request = AiRequest.Create("map.test", "Extract.", "Some transcript.", maxOutputTokens: 200) with { JsonSchema = Schema };

        var response = await provider.GenerateAsync(request, progress, CancellationToken.None);

        Assert.True(response.Json!.Value.GetProperty("ok").GetBoolean());
        Assert.Equal(AiStopReason.Completed, response.StopReason);
        Assert.Equal("eog", response.ProviderStopReason);
        Assert.Equal("local", response.ProviderId);
        Assert.Equal(LocalModelCatalog.Ministral3ThreeB, response.Model);
        Assert.Equal(TimeSpan.FromMilliseconds(15), response.Timings.ModelLoad);
        Assert.Equal(AiRequestHash.Compute(request, "local", LocalModelCatalog.Ministral3ThreeB), response.RequestHash);
        Assert.Contains(progress.Items, p => p.Stage == AiProgressStage.Loading);
        Assert.Equal("{\"ok\": true}", string.Concat(progress.Items.Where(p => p.Stage == AiProgressStage.Generating).Select(p => p.Delta)));
        Assert.Equal(AiProgressStage.Done, progress.Items[^1].Stage);

        var prompt = Assert.Single(_engines.Prompts);
        Assert.StartsWith("root ::= r\n", prompt.Grammar, StringComparison.Ordinal);
        Assert.Equal(0, prompt.Temperature);
        Assert.Equal(1, _engines.Disposed);
        Assert.Equal(LocalLlmJobRunner.ExitOk, await worker.Worker!);
        Assert.Contains("\"kind\":\"llm\"", worker.HostSent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABatchRunsOnOneModelLoadInOrder()
    {
        var provider = InProcess();
        var requests = Enumerable.Range(0, 3).Select(i => AiRequest.Create($"map.chunk{i}", "Extract.", $"Chunk {i}.", 100)).ToList();
        _engines.Answer = p => ("answer to " + p.Purpose, LocalLlmStopReasons.EndOfGeneration);

        var responses = await provider.GenerateManyAsync(requests, null, CancellationToken.None);

        Assert.Equal(["answer to map.chunk0", "answer to map.chunk1", "answer to map.chunk2"], responses.Select(r => r.Text));
        Assert.Equal(1, _engines.Loads);
        Assert.Equal(1, _engines.Disposed);
        Assert.NotNull(responses[0].Timings.ModelLoad);
        Assert.Null(responses[1].Timings.ModelLoad);
    }

    [Fact]
    public async Task AMissingModelIsReportedWithoutLoading()
    {
        await using var worker = new PipeWorker();
        File.Delete(_modelPath);

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(worker).GenerateAsync(AiRequest.Create("t", "s", "u"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ModelNotInstalled, error.Code);
        Assert.Equal("The local model Ministral 3 3B is not installed. Nothing left this PC and no document was changed. Download it in Settings › AI and privacy.", error.Message);
        Assert.Equal(0, _engines.Loads);
        Assert.Equal(LocalLlmJobRunner.ExitFailed, await worker.Worker!);
    }

    [Fact]
    public async Task ALoadFailureKeepsItsCodeAndCopyAcrossTheProtocol()
    {
        await using var worker = new PipeWorker();
        _engines.FailLoad = AiErrors.NotEnoughVram(LocalAiProvider.ProviderName, "Ministral 3 3B", 1L << 30, 3L << 30);

        var error = await Assert.ThrowsAsync<AiException>(() => Provider(worker).GenerateAsync(AiRequest.Create("t", "s", "u"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.NotEnoughVram, error.Code);
        Assert.Equal(_engines.FailLoad.Message, error.Message);
    }

    [Fact]
    public async Task CancellingStopsTheWorkerAndUnloads()
    {
        await using var worker = new PipeWorker();
        _engines.BlockUntilCancelled = true;
        using var cancel = new CancellationTokenSource();
        var generation = Provider(worker).GenerateAsync(AiRequest.Create("t", "s", "u"), null, cancel.Token);
        await _engines.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var started = DateTime.UtcNow;
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
        Assert.Equal(LocalLlmJobRunner.ExitCancelled, await worker.Worker!.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, _engines.Disposed);
        Assert.Contains(worker.HostSent, line => line.Contains("\"type\":\"cancel\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APromptThatDoesNotFitTheContextIsContentTooLong()
    {
        var provider = InProcess();
        var request = AiRequest.Create("t", "s", string.Join(' ', Enumerable.Repeat("word", 4000)), maxOutputTokens: 500);

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(request, null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.ContentTooLong, error.Code);
        Assert.Contains("the limit is 4,096", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATruncatedAnswerIsReturnedButNotParsed()
    {
        _engines.Answer = _ => ("{\"ok\":", LocalLlmStopReasons.MaxTokens);
        var request = AiRequest.Create("t", "s", "u", maxOutputTokens: 100) with { JsonSchema = Schema };

        var response = await InProcess().GenerateAsync(request, null, CancellationToken.None);

        Assert.Equal(AiStopReason.MaxTokens, response.StopReason);
        Assert.Null(response.Json);
    }

    [Fact]
    public async Task AWorkerThatDiesWithoutAnAnswerIsAWorkerCrash()
    {
        await using var worker = new PipeWorker();
        var client = new JsonLinesLocalLlmJobClient(_ => Task.FromResult(worker.Start(async (input, output) =>
        {
            await output.WriteAsync("{\"type\":\"ready\",\"pid\":1}\n");
            await input.ReadLineAsync();
            await output.WriteAsync("not json from a native library\n{\"type\":\"progress\",\"percent\":0}\n");
            return -1073741819;
        })));
        var provider = new LocalAiProvider(_model, _modelPath, client, EstimatingTokenCounter.Generic, () => null);

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("t", "s", "u"), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.WorkerCrashed, error.Code);
        Assert.StartsWith("The local model Ministral 3 3B stopped unexpectedly.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidStartLineIsRejected()
    {
        var runner = new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>());
        using var output = new StringWriter();

        var exit = await runner.RunJsonLinesAsync(new StringReader("{\"type\":\"start\",\"job\":{\"kind\":\"transcribe\"}}\n"), output);

        Assert.Equal(LocalLlmJobRunner.ExitInvalidJob, exit);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("\"type\":\"ready\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"code\":\"invalidJob\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATokenizeJobCountsWithTheModelTokenizer()
    {
        var runner = new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>());
        var job = Job() with { Prompts = [], TokenizeTexts = ["one two three", "four"] };

        var result = await new InProcessLocalLlmJobClient(runner).RunAsync(job, null, CancellationToken.None);

        Assert.Equal([3, 1], result.TokenCounts);
        Assert.Empty(result.Outputs);
    }

    [Fact]
    public async Task ReadinessChecksTheFileAndTheVideoMemory()
    {
        var auto = await InProcess(freeVram: 512L << 20).CheckAsync(CancellationToken.None);
        var gpuOnly = await new LocalAiProvider(_model, _modelPath, new InProcessLocalLlmJobClient(new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>())), EstimatingTokenCounter.Generic, () => 512L << 20, new LocalAiOptions { Device = LocalLlmDevices.Gpu }).CheckAsync(CancellationToken.None);
        var roomy = await InProcess(freeVram: 5L << 30).CheckAsync(CancellationToken.None);
        var missing = await new LocalAiProvider(_model with { SizeBytes = 99 }, _modelPath, new InProcessLocalLlmJobClient(new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>())), EstimatingTokenCounter.Generic, () => null).CheckAsync(CancellationToken.None);

        Assert.True(auto.IsReady);
        Assert.Contains("runs on the processor", auto.Note, StringComparison.Ordinal);
        Assert.False(gpuOnly.IsReady);
        Assert.Equal(AiErrorCodes.NotEnoughVram, gpuOnly.Problem!.Code);
        Assert.Contains("runs on the graphics card with a 4k context", roomy.Note, StringComparison.Ordinal);
        Assert.Equal(AiErrorCodes.ModelNotInstalled, missing.Problem!.Code);
        Assert.Equal(0, _engines.Loads);
    }

    private LocalAiProvider Provider(PipeWorker worker)
    {
        var runner = new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>());
        var client = new JsonLinesLocalLlmJobClient(_ => Task.FromResult(worker.Start((input, output) => runner.RunJsonLinesAsync(input, output))));
        return new LocalAiProvider(_model, _modelPath, client, EstimatingTokenCounter.Generic, () => 5L << 30);
    }

    private LocalAiProvider InProcess(long? freeVram = 5L << 30) =>
        new(_model, _modelPath, new InProcessLocalLlmJobClient(new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>())), EstimatingTokenCounter.Generic, () => freeVram);

    private LocalLlmJob Job() => new()
    {
        ModelPath = _modelPath,
        ModelId = _model.Id,
        ModelName = _model.Name,
        Profile = _model.Llm,
        Prompts = [new LocalLlmPrompt("t", "s", [new LocalLlmTurn(LocalLlmTurn.User, "u")], 10)],
    };
}
