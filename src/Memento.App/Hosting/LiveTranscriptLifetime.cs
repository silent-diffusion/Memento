using Memento.Transcription.Live;
using Microsoft.Extensions.Hosting;

namespace Memento.App.Hosting;

/// <summary>Runs the live transcript's loop (2.0) for the life of the app; it does nothing unless Settings turns it on.</summary>
internal sealed class LiveTranscriptLifetime(LiveTranscriptService live) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        live.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken) => await live.DisposeAsync();
}
