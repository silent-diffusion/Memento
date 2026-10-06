using Memento.Core.Processing;
using Memento.Core.Recording;
using Microsoft.Extensions.Hosting;

namespace Memento.App.Hosting;

/// <summary>
/// On app shutdown, stops an active recording cleanly (headers patched, state file kept so the next launch
/// finalizes it) and waits for finalizes in progress, within the host's stop timeout; then interrupts a running
/// processing stage, which is queued again and resumes at the next launch.
/// </summary>
internal sealed class RecordingLifetime(RecordingCoordinator recordings, ProcessingOrchestrator processing) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await recordings.ShutdownAsync(cancellationToken);
        await processing.StopAsync().WaitAsync(cancellationToken);
    }
}
