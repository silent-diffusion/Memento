using System.Diagnostics;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Workers;

/// <summary>
/// Real processes: a worker is put in the kill-on-close job object, so it ends when the launcher's job closes, which
/// Windows does with Memento's handles when Memento exits however it exits (closed, crashed or killed). <c>more.com</c>
/// stands in for the worker: like Memento.Worker.exe it waits on its redirected stdin.
/// </summary>
public sealed class ProcessWorkerLauncherTests
{
    private static readonly string StandIn = Path.Combine(Environment.SystemDirectory, "more.com");

    [Fact]
    public async Task AWorkerRunsInTheJobAndEndsWhenTheJobCloses()
    {
        var launcher = new ProcessWorkerLauncher(new WorkerLocation(StandIn), NullLogger<ProcessWorkerLauncher>.Instance);
        using var worker = launcher.Start();
        try
        {
            Assert.True(launcher.IsInJob(worker.Id));
            await Task.Delay(200);
            Assert.False(worker.Exited.IsCompleted);

            // As when Memento exits: the job's last handle closes, and Windows ends what is in it.
            launcher.Dispose();

            await worker.Exited.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(worker.Id));
        }
        finally
        {
            worker.Kill();
        }
    }

    [Fact]
    public void AMissingWorkerIsUnavailable()
    {
        using var launcher = new ProcessWorkerLauncher(new WorkerLocation(Path.Combine(Path.GetTempPath(), "no-such-worker.exe")), NullLogger<ProcessWorkerLauncher>.Instance);

        Assert.Throws<WorkerUnavailableException>(launcher.Start);
    }
}
