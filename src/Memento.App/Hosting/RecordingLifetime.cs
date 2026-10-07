using Memento.Core.Processing;
using Memento.Core.Recording;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>
/// On app shutdown, stops an active recording cleanly (headers patched, state file kept so the next launch
/// finalizes it) and waits for finalizes in progress, within the host's stop timeout; then interrupts a running
/// processing stage, which is queued again and resumes at the next launch.
/// </summary>
internal sealed partial class RecordingLifetime(RecordingCoordinator recordings, ProcessingOrchestrator processing, ILogger<RecordingLifetime> logger) : IHostedService
{
    private readonly ILogger<RecordingLifetime> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await recordings.ShutdownAsync(cancellationToken);
        try
        {
            await processing.StopAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Out of time: the stage is queued again in its manifest, and Windows ends its worker with Memento (job
            // object), so closing goes on.
            LogProcessingSlowToStop();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing had not stopped when the shutdown time ran out; it resumes at the next launch")]
    private partial void LogProcessingSlowToStop();
}
