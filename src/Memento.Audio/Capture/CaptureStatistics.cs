namespace Memento.Audio.Capture;

/// <summary>Counters of one capture stream since it started.</summary>
/// <param name="Packets">Packets taken from WASAPI.</param>
/// <param name="Frames">Frames taken from WASAPI.</param>
/// <param name="SilentPackets">Packets WASAPI flagged silent.</param>
/// <param name="Discontinuities">Packets flagged as following a discontinuity.</param>
/// <param name="TimestampErrors">Packets with an unreliable QPC position.</param>
/// <param name="SynthesizedFrames">Clock-timed silence frames inserted (endpoint loopback during silence).</param>
/// <param name="TrimmedFrames">Frames dropped because synthesized silence had already covered their time.</param>
/// <param name="OverrunPackets">Packets dropped because the consumer fell behind (never blocks the capture thread).</param>
/// <param name="OverrunFrames">Frames in those packets; the writer covers them with silence.</param>
/// <param name="EventTimeouts">Waits that ended without the buffer event (normal for loopback in silence).</param>
public sealed record CaptureStatistics(
    long Packets,
    long Frames,
    long SilentPackets,
    long Discontinuities,
    long TimestampErrors,
    long SynthesizedFrames,
    long TrimmedFrames,
    long OverrunPackets,
    long OverrunFrames,
    long EventTimeouts)
{
    public static CaptureStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
