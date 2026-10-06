namespace Memento.Audio.Writing;

/// <summary>
/// Decides, by capture time, which frames of a packet belong in the track: frames before the start, inside a
/// pause, or at/after the end are excluded. Working on capture time (not arrival time) means a packet captured
/// just before a pause but delivered just after it is still kept, so every track cuts at the same instant.
/// Times are <see cref="QpcClock"/> ticks. Not thread-safe.
/// </summary>
internal sealed class TimeGate
{
    private readonly List<(long From, long To)> _pauses = [];

    public long Start { get; private set; } = long.MinValue;

    public long End { get; private set; } = long.MaxValue;

    public bool IsPaused => _pauses.Count > 0 && _pauses[^1].To == long.MaxValue;

    public IReadOnlyList<(long From, long To)> Pauses => _pauses;

    public void SetStart(long qpc) => Start = qpc;

    public void SetEnd(long qpc) => End = Math.Min(End, qpc);

    public void Pause(long qpc)
    {
        if (IsPaused)
        {
            return;
        }

        _pauses.Add((qpc, long.MaxValue));
    }

    /// <summary>Closes the open pause; returns its duration in ticks, or 0 if not paused.</summary>
    public long Resume(long qpc)
    {
        if (!IsPaused)
        {
            return 0;
        }

        var from = _pauses[^1].From;
        var to = Math.Max(from, qpc);
        _pauses[^1] = (from, to);
        return to - from;
    }

    /// <summary>
    /// Fills <paramref name="kept"/> with the frame ranges of a packet (first frame captured at
    /// <paramref name="qpc"/>, <paramref name="frames"/> frames) that pass the gate.
    /// </summary>
    public void Split(long qpc, int frames, int sampleRate, List<FrameRange> kept)
    {
        kept.Clear();
        var lo = IndexAtOrAfter(Start, qpc, frames, sampleRate);
        var hi = IndexAtOrAfter(End, qpc, frames, sampleRate);
        if (lo >= hi)
        {
            return;
        }

        kept.Add(new FrameRange(lo, hi - lo));
        foreach (var (from, to) in _pauses)
        {
            var a = IndexAtOrAfter(from, qpc, frames, sampleRate);
            var b = IndexAtOrAfter(to, qpc, frames, sampleRate);
            if (a >= b)
            {
                continue;
            }

            for (var i = kept.Count - 1; i >= 0; i--)
            {
                var r = kept[i];
                var rEnd = r.Offset + r.Count;
                if (b <= r.Offset || a >= rEnd)
                {
                    continue;
                }

                kept.RemoveAt(i);
                if (b < rEnd)
                {
                    kept.Insert(i, new FrameRange(b, rEnd - b));
                }

                if (a > r.Offset)
                {
                    kept.Insert(i, new FrameRange(r.Offset, a - r.Offset));
                }
            }
        }
    }

    /// <summary>Index of the first frame captured at or after <paramref name="boundary"/>, clamped to [0, frames].</summary>
    internal static int IndexAtOrAfter(long boundary, long qpc, int frames, int sampleRate)
    {
        if (boundary == long.MinValue || boundary <= qpc)
        {
            return 0;
        }

        if (boundary == long.MaxValue)
        {
            return frames;
        }

        // Frame i is captured at qpc + i * 1e7 / rate; find the smallest i with that >= boundary.
        var scaled = (Int128)(boundary - qpc) * sampleRate;
        var index = (scaled + QpcClock.TicksPerSecond - 1) / QpcClock.TicksPerSecond;
        return (int)Int128.Min(index, frames);
    }
}
