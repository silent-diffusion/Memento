namespace Memento.Video.Sources;

/// <summary>Minimum, average and maximum of a series of 100 ns delays (zeros when empty).</summary>
public sealed record LatencyStatistics(long Count, long MinTicks, double AverageTicks, long MaxTicks)
{
    public static LatencyStatistics Empty { get; } = new(0, 0, 0, 0);
}
