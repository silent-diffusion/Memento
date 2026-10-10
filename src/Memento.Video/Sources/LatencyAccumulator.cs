namespace Memento.Video.Sources;

/// <summary>Accumulates delays for <see cref="LatencyStatistics"/>. Not thread-safe; callers hold their own lock.</summary>
internal sealed class LatencyAccumulator
{
    private long _count;
    private long _min = long.MaxValue;
    private long _max = long.MinValue;
    private double _sum;

    public void Add(long ticks)
    {
        _count++;
        _sum += ticks;
        _min = Math.Min(_min, ticks);
        _max = Math.Max(_max, ticks);
    }

    public LatencyStatistics Snapshot() => _count == 0 ? LatencyStatistics.Empty : new LatencyStatistics(_count, _min, _sum / _count, _max);
}
