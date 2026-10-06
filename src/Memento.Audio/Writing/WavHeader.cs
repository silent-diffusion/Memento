using System.Buffers.Binary;

namespace Memento.Audio.Writing;

/// <summary>
/// Builds and parses the canonical RIFF/WAVE header we write: <c>RIFF</c>, <c>fmt </c>, then <c>data</c> last,
/// so audio is appended at the end of the file and only two 32-bit size fields need patching.
/// </summary>
internal static class WavHeader
{
    public const int RiffSizeOffset = 4;

    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatIeeeFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;

    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Header bytes with zero sizes, and the offset of the data chunk's size field.</summary>
    public static byte[] Build(AudioFormat format, out int dataSizeOffset)
    {
        // WAVE_FORMAT_EXTENSIBLE for more than 16 bits or 2 channels (Microsoft's guidance); plain PCM/float otherwise.
        var extensible = format.BitsPerSample > 16 || format.Channels > 2 || format.ValidBitsPerSample != format.BitsPerSample;
        var fmtSize = extensible ? 40 : format.IsFloat ? 18 : 16;
        var header = new byte[12 + 8 + fmtSize + 8];
        var span = header.AsSpan();
        "RIFF"u8.CopyTo(span);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], fmtSize);
        var fmt = span.Slice(20, fmtSize);
        var tag = extensible ? WaveFormatExtensible : format.IsFloat ? WaveFormatIeeeFloat : WaveFormatPcm;
        BinaryPrimitives.WriteUInt16LittleEndian(fmt, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt[2..], (ushort)format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(fmt[4..], format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(fmt[8..], format.BytesPerSecond);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt[12..], (ushort)format.BlockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt[14..], (ushort)format.BitsPerSample);
        if (extensible)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(fmt[16..], 22);
            BinaryPrimitives.WriteUInt16LittleEndian(fmt[18..], (ushort)format.ValidBitsPerSample);
            BinaryPrimitives.WriteUInt32LittleEndian(fmt[20..], ChannelMask(format.Channels));
            (format.IsFloat ? SubtypeIeeeFloat : SubtypePcm).TryWriteBytes(fmt[24..]);
        }

        // cbSize = 0 for plain float (fmt of 18 bytes) is already zero.
        dataSizeOffset = 20 + fmtSize + 4;
        "data"u8.CopyTo(span[(20 + fmtSize)..]);
        return header;
    }

    /// <summary>Parses the header of <paramref name="stream"/> (positioned anywhere); does not trust the declared sizes.</summary>
    public static ParsedHeader Parse(Stream stream, string displayName)
    {
        var length = stream.Length;
        if (length < 12)
        {
            throw new InvalidDataException($"{displayName} is {length} bytes, too short to hold a WAV header; there is no audio to recover.");
        }

        stream.Position = 0;
        Span<byte> head = stackalloc byte[12];
        stream.ReadExactly(head);
        if (!head[..4].SequenceEqual("RIFF"u8) || !head.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException($"{displayName} is not a RIFF/WAVE file.");
        }

        AudioFormat? format = null;
        long position = 12;
        Span<byte> chunk = stackalloc byte[8];
        while (position + 8 <= length)
        {
            stream.Position = position;
            stream.ReadExactly(chunk);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            var body = position + 8;
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + size > length)
                {
                    throw new InvalidDataException($"{displayName} has a damaged format chunk.");
                }

                var fmtBytes = new byte[size];
                stream.ReadExactly(fmtBytes);
                format = ParseFormat(fmtBytes, displayName);
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (format is null)
                {
                    throw new InvalidDataException($"{displayName} has its data chunk before the format chunk.");
                }

                return new ParsedHeader(format, body, position + 4, size);
            }

            position = body + size + (size & 1);
        }

        throw new InvalidDataException($"{displayName} has no data chunk; there is no audio to recover.");
    }

    private static AudioFormat ParseFormat(ReadOnlySpan<byte> fmt, string displayName)
    {
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
        var valid = 0;
        var isFloat = tag == WaveFormatIeeeFloat;
        if (tag == WaveFormatExtensible)
        {
            if (fmt.Length < 40)
            {
                throw new InvalidDataException($"{displayName} declares WAVE_FORMAT_EXTENSIBLE with a short format chunk.");
            }

            valid = BinaryPrimitives.ReadUInt16LittleEndian(fmt[18..]);
            var subtype = new Guid(fmt.Slice(24, 16));
            isFloat = subtype == SubtypeIeeeFloat;
            if (!isFloat && subtype != SubtypePcm)
            {
                throw new InvalidDataException($"{displayName} uses an unsupported WAV subtype {subtype}.");
            }
        }
        else if (tag != WaveFormatPcm && tag != WaveFormatIeeeFloat)
        {
            throw new InvalidDataException($"{displayName} uses WAV format tag {tag}; only PCM and float are supported.");
        }

        return new AudioFormat(rate, channels, bits, isFloat ? AudioSampleEncoding.IeeeFloat : AudioSampleEncoding.Pcm, valid);
    }

    private static uint ChannelMask(int channels) => channels switch
    {
        1 => 0x4,
        2 => 0x3,
        4 => 0x33,
        6 => 0x3F,
        8 => 0x63F,
        _ => 0,
    };

    /// <summary>Header facts: where data starts, where its size lives, and what it declares.</summary>
    internal readonly record struct ParsedHeader(AudioFormat Format, long DataOffset, long DataSizeFieldOffset, uint DeclaredDataBytes);
}
