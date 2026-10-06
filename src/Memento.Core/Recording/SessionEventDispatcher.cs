using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recording;

/// <summary>
/// Runs session event handlers on the thread pool, never on the thread that raised them. Capture and writer threads
/// call <see cref="Post"/> or <see cref="PostLevels"/>, which never block: state events queue without limit (they
/// are rare); level readings keep only the newest one, so a slow consumer drops meter frames instead of growing memory.
/// A handler that throws is logged and the next event still runs.
/// </summary>
public sealed partial class SessionEventDispatcher : IAsyncDisposable
{
    private readonly Channel<Action> _events = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<Action> _levels = Channel.CreateBounded<Action>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private readonly ILogger _logger;
    private readonly Task _eventLoop;
    private readonly Task _levelLoop;

    public SessionEventDispatcher(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _eventLoop = Task.Run(() => RunAsync(_events.Reader));
        _levelLoop = Task.Run(() => RunAsync(_levels.Reader));
    }

    /// <summary>Queues a state event (pause, checkpoint, source lost, stop). Never blocks.</summary>
    public void Post(Action handler) => _events.Writer.TryWrite(handler);

    /// <summary>Queues a meter update, replacing one not yet delivered. Never blocks.</summary>
    public void PostLevels(Action handler) => _levels.Writer.TryWrite(handler);

    /// <summary>Waits until every state event queued so far has been handled.</summary>
    public Task DrainAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_events.Writer.TryWrite(() => done.TrySetResult()))
        {
            done.TrySetResult();
        }

        return done.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        _levels.Writer.TryComplete();
        await Task.WhenAll(_eventLoop, _levelLoop);
    }

    private async Task RunAsync(ChannelReader<Action> reader)
    {
        await foreach (var handler in reader.ReadAllAsync())
        {
            try
            {
                handler();
            }
#pragma warning disable CA1031 // A consumer's bug must never reach the capture path or stop later events.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogHandlerFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A recording event handler failed; recording continues")]
    private partial void LogHandlerFailed(Exception exception);
}
