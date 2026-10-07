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
    public void OutOfMemoryIsRecognisedFromTheDiagnostics()
    {
        var crash = new WorkerCrashedException(1, ["ggml_vulkan: Device memory allocation of size 1 failed.", "ErrorOutOfDeviceMemory"]);

        Assert.True(crash.LooksLikeOutOfMemory);
    }
}
