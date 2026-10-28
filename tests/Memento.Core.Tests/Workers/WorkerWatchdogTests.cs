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

    private readonly ScriptedWorkerLauncher _launcher = new();

    /// <summary>The quiet limit's clock, moved by the tests: a stalled runner cannot make a worker look hung.</summary>
    private readonly ManualTimers _timers = new();

    private readonly WorkerClientOptions _short;

    public WorkerWatchdogTests()
    {
        _short = new()
        {
            QuietLimit = TimeSpan.FromSeconds(2),
            ExitGrace = TimeSpan.FromMilliseconds(300),
            KillGrace = TimeSpan.FromMilliseconds(300),
            Time = _timers,
        };
    }

    /// <summary>
    /// Runs a job and, once the client waits for the line that never comes, lets the quiet limit pass.
    /// <paramref name="linesFirst"/> is how many lines the worker sends before it goes silent: the wait that must time
    /// out is the one the client starts after taking the last of them. Waiting for "the first timer" instead raced the
    /// worker: if a line landed after that timer was created, the clock moved while no wait was running and the next
    /// wait's limit lay beyond it (a 30 s ceiling failure on loaded runners).
    /// </summary>
    private async Task<WorkerCrashedException> HungAsync(WorkerClient client, int linesFirst = 0)
    {
        var taken = 0;
        var timersBeforeSilence = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (linesFirst == 0)
        {
            timersBeforeSilence.SetResult(0);
        }

        var run = client.RunAsync(
            GpuJob,
            _ =>
            {
                // The client calls back before it starts the next wait, so the timers counted here are all from earlier reads.
                if (Interlocked.Increment(ref taken) == linesFirst)
                {
                    timersBeforeSilence.TrySetResult(_timers.Created);
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);
        var before = await timersBeforeSilence.Task.WaitAsync(Patience.Ceiling);
        await _timers.WaitForTimersAsync(before + 1);
        _timers.Advance(_short.QuietLimit);
        return await Assert.ThrowsAsync<WorkerCrashedException>(() => run.WaitAsync(Patience.Ceiling));
    }

    [Fact]
    public async Task AWorkerThatSendsNothingForTheQuietLimitIsStoppedAsHung()
    {
        _launcher.Script = async (_, context, cancel) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Ready, Pid = 1 });
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, _short);

        var crash = await HungAsync(client, linesFirst: 1);

        Assert.True(crash.Hung);
        Assert.Equal(_short.QuietLimit, crash.QuietFor);
        Assert.False(crash.LooksLikeNativeAbort);
        Assert.Contains("sent nothing for", crash.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(_launcher.Started).Killed);
    }

    [Fact]
    public async Task TheGraphicsCardIsFreeAgainAfterAHungWorker()
    {
        // Each worker takes the script set when it is launched. A "first run" flag flipped inside the script raced: the
        // first script's body may not have run yet when its quiet limit passes, so the second worker hung too.
        _launcher.Script = async (_, _, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, _short);

        await HungAsync(client);
        _launcher.Script = (_, context, _) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return Task.FromResult(0);
        };
        var reply = await client.RunAsync(GpuJob, null, CancellationToken.None).WaitAsync(Patience.Ceiling);

        Assert.Equal(WorkerMessageTypes.Result, reply.Type);
    }

    [Fact]
    public async Task AWorkerThatKeepsReportingIsNeverStopped()
    {
        using var consumed = new SemaphoreSlim(0);
        _launcher.Script = async (_, context, _) =>
        {
            for (var i = 0; i < 10; i++)
            {
                context.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = i * 10 });

                // Once the client has the line, 1.5 s pass before the next one (on the quiet limit's clock).
                Assert.True(await consumed.WaitAsync(Patience.Ceiling, CancellationToken.None), "the client never took the line");
                _timers.Advance(TimeSpan.FromSeconds(1.5));
            }

            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return 0;
        };
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, _short);

        // 15 s in all, over seven times the 2 s limit, but never 2 s without a line.
        var reply = await client.RunAsync(
            GpuJob,
            r =>
            {
                if (r.Type == WorkerMessageTypes.Progress)
                {
                    consumed.Release();
                }

                return Task.CompletedTask;
            },
            CancellationToken.None).WaitAsync(Patience.Ceiling);

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
        using var client = new WorkerClient(_launcher, NullLogger<WorkerClient>.Instance, _short);

        var reply = await client.RunAsync(GpuJob, null, CancellationToken.None).WaitAsync(Patience.Ceiling);

        Assert.Equal(WorkerMessageTypes.Result, reply.Type);
        Assert.True(Assert.Single(_launcher.Started).Killed);
    }
}
