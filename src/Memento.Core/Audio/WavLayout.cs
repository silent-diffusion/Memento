using System.Buffers.Binary;
using System.Text;

namespace Memento.Core.Audio;

/// <summary>
/// The fixed header Memento writes: <c>RIFF</c>, a 28-byte <c>JUNK</c> chunk reserved for an RF64 <c>ds64</c>
/// chunk, a 16-byte <c>fmt </c> chunk, then <c>data</c>. The reserved chunk lets a track grow past 4 GiB
/// (a 4-hour float32 stereo track is about 5.5 GB) by rewriting the header in place as RF64.
/// </summary>
internal static class WavLayout
{
    public const int RiffSizeOffset = 4;
    public const int ReservedChunkOffset = 12;
    public const int ReservedChunkBodySize = 28;
    public const int FormatChunkOffset = 48;
    public const int DataChunkOffset = 72;
    public const int DataSizeOffset = 76;
    public const int HeaderSize = 80;

    /// <summary>Largest data size a plain RIFF header can describe for this layout.</summary>
    public const long MaxRiffDataBytes = uint.MaxValue - (HeaderSize - 8);

    public static byte[] BuildHeader(PcmFormat format, long dataBytes)
    {
        var header = new byte[HeaderSize];
        var span = header.AsSpan();
        var rf64 = dataBytes > MaxRiffDataBytes;
        var riffSize = HeaderSize - 8 + dataBytes;

        Ascii(span, 0, rf64 ? "RF64" : "RIFF");
        BinaryPrimitives.WriteUInt32LittleEndian(span[RiffSizeOffset..], rf64 ? uint.MaxValue : (uint)riffSize);
        Ascii(span, 8, "WAVE");

        Ascii(span, ReservedChunkOffset, rf64 ? "ds64" : "JUNK");
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], ReservedChunkBodySize);
        if (rf64)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[20..], (ulong)riffSize);
            BinaryPrimitives.WriteUInt64LittleEndian(span[28..], (ulong)dataBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(span[36..], (ulong)format.FramesFromBytes(dataBytes));
            BinaryPrimitives.WriteUInt32LittleEndian(span[44..], 0);
        }

        Ascii(span, FormatChunkOffset, "fmt ");
        BinaryPrimitives.WriteUInt32LittleEndian(span[52..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[56..], format.FormatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(span[58..], (ushort)format.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[60..], (uint)format.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[64..], (uint)format.BytesPerSecond);
        BinaryPrimitives.WriteUInt16LittleEndian(span[68..], (ushort)format.BlockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(span[70..], (ushort)format.BitsPerSample);

        Ascii(span, DataChunkOffset, "data");
        BinaryPrimitives.WriteUInt32LittleEndian(span[DataSizeOffset..], rf64 ? uint.MaxValue : (uint)dataBytes);
        return header;
    }

    private static void Ascii(Span<byte> span, int offset, string text) => Encoding.ASCII.GetBytes(text, span[offset..]);
}
