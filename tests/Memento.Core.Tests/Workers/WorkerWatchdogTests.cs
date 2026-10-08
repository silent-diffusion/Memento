using Memento.Core.Tests.Fakes;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Workers;

/// <summary>A worker that goes silent or will not exit cannot hold a job, or the graphics card, forever.</summary>
public sealed class WorkerWatchdogTests
{
    private static readonly WorkerJob GpuJob = new(
        WorkerJobKinds.Transcribe,
        new TranscribeJob([new WorkerTrack("mic", @"C:\x\mic.flac", 1.5)], @"C:\m\ggml-small.bin", "whisper-small", ["vulkan", "cpu"], -1, "GPU", "auto", "Hello.", 8, true, 600, 5));

    private static readonly WorkerClientOptions Short = new()
    {
        QuietLimit = TimeSpan.FromSeconds(2), // generous: a loaded machine can take longer than 300 ms to start the scripted worker
        ExitGrace = TimeSpan.FromMilliseconds(300),
        KillGrace = TimeSpan.FromMilliseconds(300),
    };

    private readonly ScriptedWorkerLauncher _launcher = new();

    [Fact]
    public async Task AWorkerThatSendsNothingForTheQuietLimitIsStoppedAsHung()
    {
        _launcher.Script = async (_, context, cancel) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Ready, Pid = 1 });
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, Short);

        var crash = await Assert.ThrowsAsync<WorkerCrashedException>(() => client.RunAsync(GpuJob, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(crash.Hung);
        Assert.Equal(Short.QuietLimit, crash.QuietFor);
        Assert.False(crash.LooksLikeNativeAbort);
        Assert.Contains("sent nothing for", crash.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(_launcher.Started).Killed);
    }

    [Fact]
    public async Task TheGraphicsCardIsFreeAgainAfterAHungWorker()
    {
        var first = true;
        _launcher.Script = async (_, context, cancel) =>
        {
            if (first)
            {
                first = false;
                await Task.Delay(Timeout.Infinite, cancel);
            }

            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, Short);

        await Assert.ThrowsAsync<WorkerCrashedException>(() => client.RunAsync(GpuJob, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        var reply = await client.RunAsync(GpuJob, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkerMessageTypes.Result, reply.Type);
    }

    [Fact]
    public async Task AWorkerThatKeepsReportingIsNeverStopped()
    {
        _launcher.Script = async (_, context, _) =>
        {
            for (var i = 0; i < 10; i++)
            {
                context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = i * 10 });
                await Task.Delay(400, CancellationToken.None);
            }

            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, Short);

        // 4 s in all, twice the 2 s limit, but never 2 s without a line.
        var reply = await client.RunAsync(GpuJob, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkerMessageTypes.Result, reply.Type);
        Assert.False(Assert.Single(_launcher.Started).Killed);
    }

    [Fact]
    public async Task AWorkerThatWillNotExitAfterItsResultDoesNotHangTheCaller()
    {
        _launcher.IgnoreKill = true;
        _launcher.Script = async (_, context, cancel) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, Short);

        var reply = await client.RunAsync(GpuJob, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkerMessageTypes.Result, reply.Type);
        Assert.True(Assert.Single(_launcher.Started).Killed);
    }
}
