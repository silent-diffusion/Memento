using System.Buffers;

namespace Memento.Audio.Capture;

/// <summary>
/// One packet from the capture loop. <see cref="Data"/> is in the stream's format and empty for silent packets.
/// The buffer is pooled: the consumer calls <see cref="Release"/> once it is done.
/// </summary>
public sealed class CapturePacket
{
    private byte[]? _buffer;

    internal CapturePacket(byte[]? pooledBuffer, int byteCount, int frames, long qpcPosition, ulong devicePosition, CapturePacketFlags flags, long droppedFramesBefore)
    {
        _buffer = pooledBuffer;
        ByteCount = pooledBuffer is null ? 0 : byteCount;
        Frames = frames;
        QpcPosition = qpcPosition;
        DevicePosition = devicePosition;
        Flags = flags;
        DroppedFramesBefore = droppedFramesBefore;
    }

    /// <summary>Interleaved samples, or empty when <see cref="IsSilent"/>.</summary>
    public ReadOnlySpan<byte> Data => _buffer is null ? default : _buffer.AsSpan(0, ByteCount);

    public int ByteCount { get; }

    public int Frames { get; }

    /// <summary>Capture time of the first frame, in <see cref="QpcClock"/> ticks.</summary>
    public long QpcPosition { get; }

    /// <summary>Device position of the first frame, in frames (0 for synthesized packets).</summary>
    public ulong DevicePosition { get; }

    public CapturePacketFlags Flags { get; }

    /// <summary>Frames dropped (consumer overrun) immediately before this packet; the writer covers them with silence.</summary>
    public long DroppedFramesBefore { get; }

    public bool IsSilent => _buffer is null;

    public bool HasReliableTimestamp => (Flags & (CapturePacketFlags.TimestampError | CapturePacketFlags.Synthesized)) == 0 && QpcPosition > 0;

    /// <summary>Creates a packet that owns a copy of <paramref name="data"/> (for tests and synthetic sources).</summary>
    public static CapturePacket FromData(ReadOnlySpan<byte> data, int frames, long qpcPosition, CapturePacketFlags flags = CapturePacketFlags.None)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, data.Length));
        data.CopyTo(buffer);
        return new CapturePacket(buffer, data.Length, frames, qpcPosition, 0, flags & ~CapturePacketFlags.Silent, 0);
    }

    /// <summary>Creates a silent packet.</summary>
    public static CapturePacket Silence(int frames, long qpcPosition, CapturePacketFlags flags = CapturePacketFlags.Silent) =>
        new(null, 0, frames, qpcPosition, 0, flags | CapturePacketFlags.Silent, 0);

    /// <summary>Returns the buffer to the pool. Idempotent.</summary>
    public void Release()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
