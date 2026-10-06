using System.Buffers.Binary;
using System.Text;

namespace Memento.Core.Audio;

/// <summary>What a WAV header says: the sample format and where the samples are.</summary>
/// <param name="DataOffset">Byte offset of the first sample.</param>
/// <param name="DeclaredDataBytes">Data size the header declares (RF64-aware); may be stale after a crash.</param>
public sealed record WavInfo(PcmFormat Format, long DataOffset, long DeclaredDataBytes, bool IsRf64)
{
    private const int MaxChunksScanned = 64;
    private const ushort FormatExtensible = 0xFFFE;

    public long DurationMs => Format.BytesToMilliseconds(DeclaredDataBytes);

    /// <summary>Reads the header of <paramref name="path"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a RIFF/RF64 WAVE with a supported format.</exception>
    public static WavInfo Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    /// <inheritdoc cref="Read(string)"/>
    public static WavInfo Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> riff = stackalloc byte[12];
        ReadExactly(stream, riff);
        var tag = Encoding.ASCII.GetString(riff[..4]);
        if (tag is not ("RIFF" or "RF64") || Encoding.ASCII.GetString(riff[8..12]) != "WAVE")
        {
            throw new InvalidDataException("The file is not a WAVE file.");
        }

        var isRf64 = tag == "RF64";
        long? ds64DataSize = null;
        PcmFormat? format = null;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> ds64 = stackalloc byte[24];
        for (var i = 0; i < MaxChunksScanned; i++)
        {
            if (stream.Read(chunkHeader) < 8)
            {
                break;
            }

            var id = Encoding.ASCII.GetString(chunkHeader[..4]);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            var bodyStart = stream.Position;
            switch (id)
            {
                case "ds64" when size >= 24:
                    ReadExactly(stream, ds64);
                    ds64DataSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(ds64[8..]);
                    break;
                case "fmt " when size >= 16:
                    format = ParseFormat(stream, (int)Math.Min(size, 40));
                    break;
                case "data":
                    if (format is null)
                    {
                        throw new InvalidDataException("The WAVE file has no format chunk before its data.");
                    }

                    var declared = isRf64 && size == uint.MaxValue && ds64DataSize is not null ? ds64DataSize.Value : size;
                    return new WavInfo(format, bodyStart, declared, isRf64);
                default:
                    break;
            }

            stream.Seek(bodyStart + size + (size & 1), SeekOrigin.Begin);
        }

        throw new InvalidDataException("The WAVE file has no data chunk.");
    }

    private static PcmFormat ParseFormat(Stream stream, int length)
    {
        Span<byte> body = stackalloc byte[40];
        ReadExactly(stream, body[..length]);
        var formatTag = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
        var sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
        if (formatTag == FormatExtensible && length >= 26)
        {
            // The sub-format GUID starts with the plain format tag.
            formatTag = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
        }

        var encoding = formatTag switch
        {
            1 => SampleEncoding.Pcm,
            3 => SampleEncoding.IeeeFloat,
            _ => throw new InvalidDataException($"WAVE format tag {formatTag} is not PCM or IEEE float."),
        };

        var format = new PcmFormat(sampleRate, channels, bits, encoding);
        try
        {
            format.Validate();
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        return format;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The WAVE header is incomplete.", ex);
        }
    }
}
