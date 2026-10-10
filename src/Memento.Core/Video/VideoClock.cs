using System.Diagnostics;

namespace Memento.Core.Video;

/// <summary>
/// The performance counter in 100 ns units: the clock audio packets and captured frames are stamped with (the same as
/// Memento.Audio's <c>QpcClock</c>), so Core can place a video track on the recording timeline.
/// </summary>
public static class VideoClock
{
    public const long TicksPerMillisecond = 10_000;

    public static long Now => FromStopwatch(Stopwatch.GetTimestamp());

    public static long FromStopwatch(long timestamp)
    {
        var frequency = Stopwatch.Frequency;
        if (frequency == TimeSpan.TicksPerSecond)
        {
            return timestamp;
        }

        var seconds = Math.DivRem(timestamp, frequency, out var remainder);
        return (seconds * TimeSpan.TicksPerSecond) + (remainder * TimeSpan.TicksPerSecond / frequency);
    }
}
