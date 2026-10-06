using Memento.Core.Host;
using Memento.Core.Status;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>Tracks the page's <c>ui.ready</c> signal; <c>--screenshot</c> waits on <see cref="Ready"/>.</summary>
internal sealed partial class UiLifecycle(FooterStatusService footer, ILogger<UiLifecycle> logger) : IUiLifecycle
{
    private readonly ILogger<UiLifecycle> _logger = logger;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes the first time the page reports ready.</summary>
    public Task Ready => _ready.Task;

    public void NotifyReady()
    {
        // A reload (e.g. after a renderer crash) reports ready again; make sure it has fresh footer values.
        if (!_ready.TrySetResult())
        {
            footer.Publish(force: true);
        }

        LogReady();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Interface reported ready")]
    private partial void LogReady();
}
