namespace Memento.Audio.Capture;

/// <summary>
/// Mutable counters written only by the capture thread (plain 64-bit stores are atomic on x64) and read as a
/// <see cref="CaptureStatistics"/> snapshot from any thread.
/// </summary>
internal sealed class CaptureCounters
{
    private long _packets;
    private long _frames;
    private long _silentPackets;
    private long _discontinuities;
    private long _timestampErrors;
    private long _synthesizedFrames;
    private long _trimmedFrames;
    private long _overrunPackets;
    private long _overrunFrames;
    private long _eventTimeouts;

    public void Packet(int frames, uint wasapiFlags)
    {
        Volatile.Write(ref _packets, _packets + 1);
        Volatile.Write(ref _frames, _frames + frames);
        if ((wasapiFlags & Interop.CoreAudio.BufferFlagsSilent) != 0)
        {
            Volatile.Write(ref _silentPackets, _silentPackets + 1);
        }

        if ((wasapiFlags & Interop.CoreAudio.BufferFlagsDataDiscontinuity) != 0)
        {
            Volatile.Write(ref _discontinuities, _discontinuities + 1);
        }

        if ((wasapiFlags & Interop.CoreAudio.BufferFlagsTimestampError) != 0)
        {
            Volatile.Write(ref _timestampErrors, _timestampErrors + 1);
        }
    }

    public void Synthesized(long frames) => Volatile.Write(ref _synthesizedFrames, _synthesizedFrames + frames);

    public void Trimmed(long frames) => Volatile.Write(ref _trimmedFrames, _trimmedFrames + frames);

    public void Overrun(long frames)
    {
        Volatile.Write(ref _overrunPackets, _overrunPackets + 1);
        Volatile.Write(ref _overrunFrames, _overrunFrames + frames);
    }

    public void EventTimeout() => Volatile.Write(ref _eventTimeouts, _eventTimeouts + 1);

    public CaptureStatistics Snapshot() => new(
        Volatile.Read(ref _packets),
        Volatile.Read(ref _frames),
        Volatile.Read(ref _silentPackets),
        Volatile.Read(ref _discontinuities),
        Volatile.Read(ref _timestampErrors),
        Volatile.Read(ref _synthesizedFrames),
        Volatile.Read(ref _trimmedFrames),
        Volatile.Read(ref _overrunPackets),
        Volatile.Read(ref _overrunFrames),
        Volatile.Read(ref _eventTimeouts));
}
