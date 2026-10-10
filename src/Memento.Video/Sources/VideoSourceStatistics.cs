namespace Memento.Video.Sources;

/// <summary>A snapshot of what a source delivered.</summary>
/// <param name="FramesArrived">Pictures Windows delivered (a display delivers only when something on it changes, up to its refresh rate).</param>
/// <param name="FirstFrameQpc">QPC time of the first picture, 0 before it.</param>
/// <param name="LastFrameQpc">QPC time of the latest picture, 0 before it.</param>
/// <param name="ArrivalLatency">Delay between each picture's own time stamp and its arrival in Memento, in 100 ns ticks.</param>
public sealed record VideoSourceStatistics(long FramesArrived, long FirstFrameQpc, long LastFrameQpc, LatencyStatistics ArrivalLatency, int Width, int Height)
{
    public static VideoSourceStatistics Empty { get; } = new(0, 0, 0, LatencyStatistics.Empty, 0, 0);
}
