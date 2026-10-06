namespace Memento.Audio.Recording;

/// <summary>
/// Maps QPC time to recording-timeline time: elapsed since the session start minus every pause before it.
/// Pauses are excluded from all tracks, so this is also the position inside a track that started with the session.
/// Thread-safe.
/// </summary>
internal sealed class SessionTimeline(long startQpc)
{
    private readonly object _sync = new();
    private readonly List<(long From, long To)> _pauses = [];

    public long StartQpc { get; } = startQpc;

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _pauses.Count > 0 && _pauses[^1].To == long.MaxValue;
            }
        }
    }

    /// <summary>QPC time the open pause began, or null.</summary>
    public long? PausedSince
    {
        get
        {
            lock (_sync)
            {
                return _pauses.Count > 0 && _pauses[^1].To == long.MaxValue ? _pauses[^1].From : null;
            }
        }
    }

    public bool Pause(long qpc)
    {
        lock (_sync)
        {
            if (_pauses.Count > 0 && _pauses[^1].To == long.MaxValue)
            {
                return false;
            }

            _pauses.Add((Math.Max(qpc, StartQpc), long.MaxValue));
            return true;
        }
    }

    /// <summary>Ends the open pause; returns (timeline position, duration) of the gap, or null if not paused.</summary>
    public (TimeSpan At, TimeSpan Duration)? Resume(long qpc)
    {
        lock (_sync)
        {
            if (_pauses.Count == 0 || _pauses[^1].To != long.MaxValue)
            {
                return null;
            }

            var from = _pauses[^1].From;
            var to = Math.Max(from, qpc);
            _pauses[^1] = (from, to);
            return (ToTimelineLocked(from), TimeSpan.FromTicks(to - from));
        }
    }

    public TimeSpan ToTimeline(long qpc)
    {
        lock (_sync)
        {
            return ToTimelineLocked(qpc);
        }
    }

    public IReadOnlyList<(TimeSpan At, TimeSpan Duration)> Gaps()
    {
        lock (_sync)
        {
            return _pauses.Where(p => p.To != long.MaxValue).Select(p => (ToTimelineLocked(p.From), TimeSpan.FromTicks(p.To - p.From))).ToList();
        }
    }

    private TimeSpan ToTimelineLocked(long qpc)
    {
        var t = Math.Max(qpc, StartQpc);
        var elapsed = t - StartQpc;
        foreach (var (from, to) in _pauses)
        {
            if (from >= t)
            {
                break;
            }

            elapsed -= Math.Min(to, t) - from;
        }

        return TimeSpan.FromTicks(Math.Max(0, elapsed));
    }
}
