using Memento.Core.Bridge;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Video;

/// <summary>
/// <c>video.sourcesChanged</c>: while the page is listing video sources (the picker or the VIDEO section is open, which
/// calls <c>video.listSources</c>), the sources are listed again every <see cref="Interval"/> and the page is told when
/// a display, window or camera came or went. It stops by itself a minute after the last listing, so nothing is polled
/// while no one looks. Listing opens nothing.
/// </summary>
public sealed partial class VideoSourceWatch(IVideoCapture capture, BridgeEventPublisher publisher, TimeProvider time, ILogger<VideoSourceWatch> logger) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(1);

    private readonly ILogger<VideoSourceWatch> _logger = logger;
    private readonly object _sync = new();
    private ITimer? _timer;
    private DateTimeOffset _lastListed;
    private string? _lastKey;
    private int _ticking;

    /// <summary>The page listed the sources (with the ids it got); watching starts or carries on.</summary>
    public void Listed(Bridge.Contracts.VideoSourcesResult sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        lock (_sync)
        {
            _lastListed = time.GetUtcNow();
            _lastKey = Key(sources);
            _timer ??= time.CreateTimer(_ => Tick(), null, Interval, Interval);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    internal static string Key(Bridge.Contracts.VideoSourcesResult sources) =>
        string.Join('\n', sources.Displays.Concat(sources.Windows).Concat(sources.Cameras).Select(s => s.Id + "|" + s.Name + "|" + s.Excluded).Order(StringComparer.Ordinal));

    /// <summary>One look at the sources (the timer calls it; tests call it directly).</summary>
    internal void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                if (time.GetUtcNow() - _lastListed > IdleAfter)
                {
                    _timer?.Dispose();
                    _timer = null;
                    return;
                }
            }

            var key = Key(capture.ListSources());
            bool changed;
            lock (_sync)
            {
                changed = _lastKey is not null && !string.Equals(_lastKey, key, StringComparison.Ordinal);
                _lastKey = key;
            }

            if (changed)
            {
                publisher.PublishVideoSourcesChanged();
            }
        }
#pragma warning disable CA1031 // A failed listing is tried again at the next tick.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogListFailed(ex);
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing the video sources for the picker failed")]
    private partial void LogListFailed(Exception exception);
}
