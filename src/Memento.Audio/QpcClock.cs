using System.Diagnostics;

namespace Memento.Audio;

/// <summary>
/// The performance counter in 100-nanosecond units, the same time base WASAPI uses for
/// <c>IAudioCaptureClient::GetBuffer</c>'s QPC position. Every track timestamp is expressed in it.
/// </summary>
public static class QpcClock
{
    public const long TicksPerSecond = 10_000_000;

    public const long TicksPerMillisecond = 10_000;

    /// <summary>Current performance-counter time in 100 ns ticks.</summary>
    public static long Now => FromStopwatchTimestamp(Stopwatch.GetTimestamp());

    public static long FromStopwatchTimestamp(long timestamp)
    {
        var frequency = Stopwatch.Frequency;
        if (frequency == TicksPerSecond)
        {
            return timestamp;
        }

        // Split to avoid overflow: timestamp * 1e7 exceeds long for long uptimes.
        var seconds = Math.DivRem(timestamp, frequency, out var remainder);
        return (seconds * TicksPerSecond) + (remainder * TicksPerSecond / frequency);
    }

    /// <summary>Duration of <paramref name="frames"/> at <paramref name="sampleRate"/> in 100 ns ticks (rounded down).</summary>
    public static long FramesToTicks(long frames, int sampleRate) => (long)((Int128)frames * TicksPerSecond / sampleRate);

    /// <summary>Whole frames that fit in <paramref name="ticks"/> (rounded down; negative input gives a negative result).</summary>
    public static long TicksToFrames(long ticks, int sampleRate) => (long)((Int128)ticks * sampleRate / TicksPerSecond);

    /// <summary>Frames in <paramref name="ticks"/> rounded to the nearest frame.</summary>
    public static long TicksToFramesRounded(long ticks, int sampleRate)
    {
        var scaled = (Int128)ticks * sampleRate;
        var half = TicksPerSecond / 2;
        return (long)(scaled >= 0 ? (scaled + half) / TicksPerSecond : (scaled - half) / TicksPerSecond);
    }

    public static long TicksToMilliseconds(long ticks) => ticks / TicksPerMillisecond;
}
