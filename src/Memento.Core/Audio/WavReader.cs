using System.Buffers.Binary;

namespace Memento.Core.Audio;

/// <summary>
/// Reads a WAV file's samples as interleaved floats in −1..1, whatever the stored encoding.
/// Reads at most what the header declares and what the file actually holds.
/// </summary>
public sealed class WavReader : IDisposable
{
    private const int ReadBufferBytes = 64 * 1024;

    private readonly FileStream _stream;
    private readonly byte[] _buffer;
    private readonly int _bytesPerSample;
    private long _remainingBytes;

    public WavReader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);
        try
        {
            Info = WavInfo.Read(_stream);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }

        var available = Math.Max(0, _stream.Length - Info.DataOffset);
        var dataBytes = Math.Min(Info.DeclaredDataBytes, available);
        dataBytes -= dataBytes % Format.BlockAlign;
        _remainingBytes = dataBytes;
        TotalFrames = Format.FramesFromBytes(dataBytes);
        _bytesPerSample = Format.BitsPerSample / 8;
        _buffer = new byte[ReadBufferBytes - (ReadBufferBytes % Format.BlockAlign)];
        _stream.Seek(Info.DataOffset, SeekOrigin.Begin);
    }

    public WavInfo Info { get; }

    public PcmFormat Format => Info.Format;

    public long TotalFrames { get; }

    /// <summary>Fills <paramref name="destination"/> with whole frames; returns the number of frames read (0 at the end).</summary>
    public int ReadFrames(Span<float> destination)
    {
        var channels = Format.Channels;
        var maxFrames = destination.Length / channels;
        var framesRead = 0;
        while (framesRead < maxFrames && _remainingBytes > 0)
        {
            var wantBytes = (int)Math.Min(Math.Min(_buffer.Length, (long)(maxFrames - framesRead) * Format.BlockAlign), _remainingBytes);
            var got = ReadWholeFrames(wantBytes);
            if (got == 0)
            {
                _remainingBytes = 0;
                break;
            }

            _remainingBytes -= got;
            var samples = got / _bytesPerSample;
            Decode(_buffer.AsSpan(0, got), destination.Slice(framesRead * channels, samples));
            framesRead += got / Format.BlockAlign;
        }

        return framesRead;
    }

    public void Dispose() => _stream.Dispose();

    private int ReadWholeFrames(int wantBytes)
    {
        var total = 0;
        while (total < wantBytes)
        {
            var n = _stream.Read(_buffer, total, wantBytes - total);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total - (total % Format.BlockAlign);
    }

    private void Decode(ReadOnlySpan<byte> bytes, Span<float> destination)
    {
        switch (Format.Encoding, Format.BitsPerSample)
        {
            case (SampleEncoding.IeeeFloat, 32):
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * 4)..]);
                }

                break;
            case (SampleEncoding.Pcm, 16):
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]) / 32768f;
                }

                break;
            case (SampleEncoding.Pcm, 24):
                for (var i = 0; i < destination.Length; i++)
                {
                    var o = i * 3;
                    var value = bytes[o] | (bytes[o + 1] << 8) | ((sbyte)bytes[o + 2] << 16);
                    destination[i] = value / 8388608f;
                }

                break;
            case (SampleEncoding.Pcm, 32):
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(i * 4)..]) / 2147483648f;
                }

                break;
            case (SampleEncoding.Pcm, 8):
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = (bytes[i] - 128) / 128f;
                }

                break;
            default:
                throw new InvalidDataException($"Cannot decode {Format}.");
        }
    }
}
