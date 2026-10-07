using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Memento.AI.Local;
using Memento.AI.Tests.Fakes;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.AI.Tests.Local;

/// <summary>
/// The local provider through Core's <see cref="WorkerClient"/> (the production path): the job travels as
/// <c>{"kind":"llm","llm":{…}}</c>, the worker side is the real <see cref="LocalLlmJobRunner"/> protocol on a fake engine
/// behind in-process pipes, and every line comes back through Core's <see cref="WorkerReply"/>.
/// </summary>
public sealed class WorkerLocalLlmJobClientTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("memento-ai-worker-").FullName;
    private readonly FakeLocalEngineFactory _engines = new();
    private readonly PipeLauncher _launcher;
    private readonly WorkerClient _workers;
    private readonly LocalModelEntry _model;
    private readonly string _modelPath;

    public WorkerLocalLlmJobClientTests()
    {
        _modelPath = Path.Combine(_folder, "model.gguf");
        File.WriteAllBytes(_modelPath, [1, 2, 3, 4]);
        _model = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)! with { SizeBytes = 4 };
        _launcher = new PipeLauncher(new LocalLlmJobRunner(_engines, NullLogger<LocalLlmJobRunner>.Instance));
        _workers = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance);
    }

    public void Dispose()
    {
        _workers.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ABatchRoundTripsThroughCoresWorkerProtocol()
    {
        var provider = Provider(LocalLlmDevices.Cpu);
        var progress = new ProgressLog<AiProgress>();
        var requests = new[]
        {
            AiRequest.Create("map.decisions", "Extract.", "first", 100),
            AiRequest.Create("verify.claim", "Check.", "second", 100),
        };

        var responses = await provider.GenerateManyAsync(requests, progress, CancellationToken.None);

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.Equal(AiStopReason.Completed, r.StopReason));
        Assert.Equal(1, _engines.Loads);
        Assert.Contains(progress.Items, p => p.Stage == AiProgressStage.Loading);
        Assert.Contains(progress.Items, p => p.Stage == AiProgressStage.Generating && p.Delta is not null);
        var sent = Assert.Single(_launcher.FirstLines);
        using var start = JsonDocument.Parse(sent);
        Assert.Equal("start", start.RootElement.GetProperty("type").GetString());
        Assert.Equal(WorkerJobKinds.Llm, start.RootElement.GetProperty("job").GetProperty("kind").GetString());
        Assert.Equal("cpu", start.RootElement.GetProperty("job").GetProperty("llm").GetProperty("device").GetString());
        Assert.Equal(2, start.RootElement.GetProperty("job").GetProperty("llm").GetProperty("prompts").GetArrayLength());
    }

    [Fact]
    public async Task AWorkerErrorKeepsItsAiCodeAndCopy()
    {
        _engines.FailLoad = AiErrors.NotEnoughVram(LocalAiProvider.ProviderName, _model.Name, 1L << 30, 3L << 30);
        var provider = Provider(LocalLlmDevices.Gpu);

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("map.x", "s", "u", 50), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.NotEnoughVram, error.Code);
        Assert.Contains("Nothing left this PC", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWorkerThatEndsWithoutAResultIsACrash()
    {
        _launcher.Script = (_, writer) => writer.WriteLineAsync("{\"type\":\"ready\",\"pid\":1}").ContinueWith(_ => 1, TaskScheduler.Default);
        var provider = Provider(LocalLlmDevices.Cpu);

        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("map.x", "s", "u", 50), null, CancellationToken.None));

        Assert.Equal(AiErrorCodes.WorkerCrashed, error.Code);
    }

    [Fact]
    public async Task CancellingSendsCancelAndEndsTheJob()
    {
        _engines.BlockUntilCancelled = true;
        var provider = Provider(LocalLlmDevices.Cpu);
        using var cancel = new CancellationTokenSource();

        var run = provider.GenerateAsync(AiRequest.Create("map.x", "s", "u", 50), null, cancel.Token);
        await _engines.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public void OnlyProcessorJobsSkipTheGraphicsCardGate()
    {
        static WorkerJob Job(string device) => new(WorkerJobKinds.Llm, Llm: JsonDocument.Parse($$"""{"device":"{{device}}"}""").RootElement.Clone());

        Assert.False(Job(LocalLlmDevices.Cpu).UsesGpu);
        Assert.True(Job(LocalLlmDevices.Gpu).UsesGpu);
        Assert.True(Job(LocalLlmDevices.Auto).UsesGpu);
    }

    [Fact]
    public void WorkerLinesBecomeLocalProtocolLines()
    {
        var device = JsonSerializer.SerializeToElement(new LocalLlmDeviceInfo("vulkan", "GPU", 33, 8192, 8), LocalLlmJsonContext.Default.LocalLlmDeviceInfo);
        var line = new WorkerReply { Type = WorkerMessageTypes.Device, LlmDevice = device };

        var local = WorkerLocalLlmJobClient.ToLocal(line);

        Assert.Equal("vulkan", local!.LlmDevice!.Backend);
        Assert.Null(WorkerLocalLlmJobClient.ToLocal(new WorkerReply { Type = WorkerMessageTypes.Log, Message = "x" }));
    }

    private LocalAiProvider Provider(string device) =>
        new(_model, _modelPath, new WorkerLocalLlmJobClient(_workers), new EstimatingTokenCounter(1.25), () => 6L << 30, new LocalAiOptions { Device = device });

    /// <summary>Starts an in-process "worker" per job: the local runner's JSON-lines protocol over pipes.</summary>
    private sealed class PipeLauncher(LocalLlmJobRunner runner) : IWorkerLauncher
    {
        private int _next = 100;

        public Func<TextReader, TextWriter, Task<int>>? Script { get; set; }

        public List<string> FirstLines { get; } = [];

        public IWorkerProcess Start() => new PipeProcess(Interlocked.Increment(ref _next), Script ?? ((input, output) => runner.RunJsonLinesAsync(input, output)), FirstLines);
    }

    private sealed class PipeProcess : IWorkerProcess
    {
        private readonly AnonymousPipeServerStream _toWorker = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromWorker = new(PipeDirection.Out);
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PipeProcess(int id, Func<TextReader, TextWriter, Task<int>> script, List<string> firstLines)
        {
            Id = id;
            var workerIn = new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, _toWorker.ClientSafePipeHandle), new UTF8Encoding(false));
            var workerOut = new StreamWriter(_fromWorker, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            Output = new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, _fromWorker.ClientSafePipeHandle), new UTF8Encoding(false));
            Input = new FirstLineWriter(new StreamWriter(_toWorker, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }, firstLines);
            _ = Task.Run(async () =>
            {
                int code;
                try
                {
                    code = await script(workerIn, workerOut);
                }
                catch (IOException)
                {
                    code = 1;
                }

                await workerOut.DisposeAsync();
                _exited.TrySetResult(code);
            });
        }

        public int Id { get; }

        public TextWriter Input { get; }

        public TextReader Output { get; }

        public Task<int> Exited => _exited.Task;

        public TimeSpan CpuTime => TimeSpan.Zero;

        public IReadOnlyList<string> ErrorTail => [];

        public void Kill()
        {
            _toWorker.Dispose();
            _exited.TrySetResult(-1);
        }

        public void Dispose()
        {
            Input.Dispose();
            _toWorker.Dispose();
        }
    }

    private sealed class FirstLineWriter(TextWriter inner, List<string> firstLines) : TextWriter
    {
        private bool _seen;

        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value) => inner.Write(value);

        public override async Task WriteLineAsync(string? value)
        {
            if (!_seen)
            {
                _seen = true;
                lock (firstLines)
                {
                    firstLines.Add(value ?? string.Empty);
                }
            }

            try
            {
                await inner.WriteLineAsync(value);
            }
            catch (ObjectDisposedException)
            {
                throw new IOException("The worker has gone.");
            }
        }

        public override async Task FlushAsync()
        {
            try
            {
                await inner.FlushAsync();
            }
            catch (ObjectDisposedException)
            {
                throw new IOException("The worker has gone.");
            }
        }
    }
}
