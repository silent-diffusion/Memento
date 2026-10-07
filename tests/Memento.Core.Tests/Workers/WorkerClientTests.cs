using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Workers;

/// <summary>The JSON-lines protocol against a scripted worker.</summary>
public sealed class WorkerClientTests
{
    private static readonly WorkerJob Job = new(
        WorkerJobKinds.Transcribe,
        new TranscribeJob([new WorkerTrack("mic", @"C:\x\mic.flac", 1.5, 2)], @"C:\m\ggml-small.bin", "whisper-small", ["vulkan", "cpu"], -1, "GPU", "auto", "Hello.", 8, true, 600, 5));

    private readonly ScriptedWorkerLauncher _launcher = new();

    private WorkerClient Client => new(_launcher, NullLogger<WorkerClient>.Instance);

    [Fact]
    public async Task SendsTheJobPassesEveryLineInOrderAndReturnsTheResult()
    {
        _launcher.Script = (job, context, _) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Ready, Pid = 1 });
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Device, Device = new WorkerDevice("vulkan", "GPU (Vulkan)", "GPU", 0, "1.9.1") });
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 50, TrackId = "mic", WindowsDone = 3, Segments = [new WorkerSegment(1, 2, "Hi.", 0.9, [new TranscriptWord("Hi.", 1, 2, 0.9)])] });
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Log, Message = "ignored by the caller" });
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result, Transcription = new TranscribeResult("en", true, new WorkerDevice("vulkan", "GPU (Vulkan)", "GPU", 0, "1.9.1"), 60, 1234) });
            return Task.FromResult(0);
        };
        var seen = new List<string>();

        var result = await Client.RunAsync(Job, r => { seen.Add(r.Type); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(["ready", "device", "progress"], seen);
        Assert.Equal("en", result.Transcription!.Language);
        var started = Assert.Single(_launcher.Started);
        var command = JsonDocument.Parse(started.Received[0]).RootElement;
        Assert.Equal("start", command.GetProperty("type").GetString());
        Assert.Equal("transcribe", command.GetProperty("job").GetProperty("kind").GetString());
        var transcribe = command.GetProperty("job").GetProperty("transcribe");
        Assert.Equal(2, transcribe.GetProperty("tracks")[0].GetProperty("startWindow").GetInt32());
        Assert.Equal("""["vulkan","cpu"]""", transcribe.GetProperty("runtimes").GetRawText());
        Assert.False(command.GetProperty("job").TryGetProperty("diarize", out _));
    }

    [Fact]
    public async Task AnErrorLineBecomesAStructuredException()
    {
        _launcher.Script = (_, context, _) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.ModelLoad, Message = "bad magic" });
            return Task.FromResult(1);
        };

        var error = await Assert.ThrowsAsync<WorkerJobException>(() => Client.RunAsync(Job, null, CancellationToken.None));

        Assert.Equal(WorkerErrorCodes.ModelLoad, error.Code);
        Assert.Equal("bad magic", error.Message);
    }

    [Fact]
    public async Task AWorkerThatDiesWithoutAResultIsReportedAsACrash()
    {
        _launcher.Script = (_, context, _) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 10 });
            return Task.FromResult(unchecked((int)0xC0000409));
        };

        var crash = await Assert.ThrowsAsync<WorkerCrashedException>(() => Client.RunAsync(Job, null, CancellationToken.None));

        Assert.Equal(unchecked((int)0xC0000409), crash.ExitCode);
        Assert.True(crash.LooksLikeNativeAbort);
        Assert.False(crash.LooksLikeOutOfMemory);
    }

    [Fact]
    public async Task LinesThatAreNotProtocolAreIgnored()
    {
        _launcher.Script = (_, context, _) =>
        {
            context.SendRaw("whisper_init_from_file: loading model");
            context.SendRaw("{\"type\":\"progress\",\"percent\":5}");
            context.SendRaw("{\"type\":\"progress\", broken");
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return Task.FromResult(0);
        };
        var seen = 0;

        await Client.RunAsync(Job, _ => { seen++; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task CancellingSendsCancelAndTheWorkerStops()
    {
        _launcher.Script = async (_, context, cancel) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 1 });
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        using var cancel = new CancellationTokenSource();

        var run = Client.RunAsync(Job, _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var process = Assert.Single(_launcher.Started);
        Assert.True(process.CancelReceived);
        Assert.Contains(process.Received, l => l.Contains("\"cancel\"", StringComparison.Ordinal));
        Assert.False(process.Killed);
    }

    [Fact]
    public async Task AWorkerThatIgnoresCancelIsKilled()
    {
        _launcher.Script = async (_, context, _) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 1 });
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            return 0;
        };
        using var cancel = new CancellationTokenSource();

        var run = Client.RunAsync(Job, _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.True(Assert.Single(_launcher.Started).Killed);
    }

    [Fact]
    public async Task AMissingWorkerIsUnavailable()
    {
        _launcher.StartFailure = new WorkerUnavailableException("missing");

        await Assert.ThrowsAsync<WorkerUnavailableException>(() => Client.RunAsync(Job, null, CancellationToken.None));
    }

    [Fact]
    public void ReplyLinesStartWithTheTypeAndLeaveOutNulls()
    {
        var json = JsonSerializer.Serialize(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 12.5 }, WorkerJsonContext.Default.WorkerReply);

        Assert.Equal("""{"type":"progress","percent":12.5}""", json);
    }

    [Fact]
    public async Task OnlyOneGraphicsCardJobRunsAtATimeAndTheNextStartsOnceTheFirstWorkerHasExited()
    {
        var release = new TaskCompletionSource();
        var running = 0;
        var most = 0;
        _launcher.Script = async (job, context, _) =>
        {
            var now = Interlocked.Increment(ref running);
            most = Math.Max(most, now);
            if (job.UsesGpu && now == 1 && !release.Task.IsCompleted)
            {
                await release.Task;
            }

            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result, Transcription = new TranscribeResult("en", true, new WorkerDevice("vulkan", "GPU (Vulkan)", "GPU", 0, "1.9.1"), 1, 1) });
            await Task.Delay(50, CancellationToken.None);
            Interlocked.Decrement(ref running);
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance);

        var first = client.RunAsync(Job, null, CancellationToken.None);
        await TestRecordings.WaitUntilAsync(() => _launcher.Started.Count == 1, "the first worker");
        var second = client.RunAsync(Job, null, CancellationToken.None);
        await Task.Delay(200);

        // The second GPU job has not even started a worker while the first runs.
        Assert.Single(_launcher.Started);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, _launcher.Started.Count);
        Assert.True(_launcher.Started[0].Exited.IsCompleted);
        Assert.Equal(1, most);
    }

    [Fact]
    public async Task ProcessorJobsDoNotWaitForTheGraphicsCard()
    {
        var release = new TaskCompletionSource();
        _launcher.Script = async (job, context, _) =>
        {
            if (job.UsesGpu)
            {
                await release.Task;
            }

            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance);
        var cpuJob = Job with { Transcribe = Job.Transcribe! with { Runtimes = ["cpu"] } };
        var diarize = new WorkerJob(WorkerJobKinds.Diarize, Diarize: new DiarizeJob([new WorkerTrack("mic", @"C:\x\mic.flac", 0)], "s.onnx", "e.onnx", -1, 0.8f, 4));

        var gpu = client.RunAsync(Job, null, CancellationToken.None);
        await client.RunAsync(cpuJob, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await client.RunAsync(diarize, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(Job.UsesGpu);
        Assert.False(cpuJob.UsesGpu);
        Assert.False(diarize.UsesGpu);
        Assert.False(gpu.IsCompleted);
        release.SetResult();
        await gpu.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AWorkerLeftBehindByAFailingCallerIsKilledAndAwaitedBeforeTheNextGpuJob()
    {
        _launcher.Script = async (_, context, cancel) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = 1 });
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance);

        await Assert.ThrowsAsync<IOException>(() => client.RunAsync(Job, _ => throw new IOException("disk full"), CancellationToken.None));

        var left = Assert.Single(_launcher.Started);
        Assert.True(left.Killed);
        Assert.True(left.Exited.IsCompleted);
    }

    [Fact]
    public void OutOfMemoryIsRecognisedFromTheDiagnostics()
    {
        var crash = new WorkerCrashedException(1, ["ggml_vulkan: Device memory allocation of size 1 failed.", "ErrorOutOfDeviceMemory"]);

        Assert.True(crash.LooksLikeOutOfMemory);
    }
}
