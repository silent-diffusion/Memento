using Memento.Core.Recording;
using Microsoft.Extensions.Hosting;

namespace Memento.App.Hosting;

/// <summary>
/// On app shutdown, stops an active recording cleanly (headers patched, state file kept so the next launch
/// finalizes it) and waits for finalizes in progress, within the host's stop timeout.
/// </summary>
internal sealed class RecordingLifetime(RecordingCoordinator recordings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => recordings.ShutdownAsync(cancellationToken);
}
