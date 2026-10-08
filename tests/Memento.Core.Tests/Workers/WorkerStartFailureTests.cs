using Memento.Core.Tests.Fakes;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Workers;

/// <summary>A worker that dies before it reads its job is a crash with an exit code, not a raw pipe error.</summary>
public sealed class WorkerStartFailureTests
{
    private static readonly WorkerJob Job = new(
        WorkerJobKinds.Diarize,
        Diarize: new DiarizeJob([new WorkerTrack("mic", @"C:\x\mic.flac", 0)], "s.onnx", "e.onnx", -1, 0.8f, 4));

    [Fact]
    public async Task ABrokenStdinAtStartIsReportedAsACrash()
    {
        var launcher = new ScriptedWorkerLauncher { InputFailure = new IOException("The pipe has been ended.") };
        using var client = new WorkerClient(launcher, NullLogger<WorkerClient>.Instance);

        var crash = await Assert.ThrowsAsync<WorkerCrashedException>(() => client.RunAsync(Job, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(unchecked((int)0xC0000005), crash.ExitCode);
        Assert.False(crash.Hung);
    }
}
