using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Memento.Audio.Writing;

/// <summary>
/// Sample conversions on the write path. Float32 capture data becomes int24 (what the FLAC encoder takes,
/// 25% smaller than float); int16 and int24 pass through; 24-in-32 and int32 are narrowed to int24.
/// </summary>
public static class PcmConverter
{
    public const int Int24Max = 8_388_607;

    public const int Int24Min = -8_388_608;

    /// <summary>The integer format a track in <paramref name="input"/> is stored as.</summary>
    public static AudioFormat StorageFormatFor(AudioFormat input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.IsFloat || input.BitsPerSample >= 24
            ? AudioFormat.Pcm24(input.SampleRate, input.Channels)
            : AudioFormat.Pcm16(input.SampleRate, input.Channels);
    }

    /// <summary>Converts one float sample to a signed 24-bit integer: clamped to [-1, 1], scaled by 8 388 607, rounded to nearest even.</summary>
    public static int FloatToInt24(float value)
    {
        if (float.IsNaN(value))
        {
            return 0;
        }

        var clamped = Math.Clamp((double)value, -1.0, 1.0);
        return (int)Math.Round(clamped * Int24Max, MidpointRounding.ToEven);
    }

    /// <summary>Writes the little-endian 3-byte form of <paramref name="value"/>.</summary>
    public static void WriteInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    public static int ReadInt24(ReadOnlySpan<byte> source) => (source[0] | (source[1] << 8) | ((sbyte)source[2] << 16));

    /// <summary>Converts float samples to packed int24.</summary>
    public static void FloatToInt24(ReadOnlySpan<float> source, Span<byte> destination)
    {
        if (destination.Length < source.Length * 3)
        {
            throw new ArgumentException("Destination is too small for the converted samples.", nameof(destination));
        }

        for (var i = 0; i < source.Length; i++)
        {
            WriteInt24(destination.Slice(i * 3, 3), FloatToInt24(source[i]));
        }
    }

    /// <summary>
    /// Converts whole frames from <paramref name="sourceFormat"/> to <see cref="StorageFormatFor"/> of it.
    /// Returns the number of bytes written to <paramref name="destination"/>.
    /// </summary>
    public static int ToStorage(ReadOnlySpan<byte> source, AudioFormat sourceFormat, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(sourceFormat);
        if (source.Length % sourceFormat.BlockAlign != 0)
        {
            throw new ArgumentException("Source must hold whole frames.", nameof(source));
        }

        var target = StorageFormatFor(sourceFormat);
        var samples = source.Length / sourceFormat.BytesPerSample;
        var needed = samples * target.BytesPerSample;
        if (destination.Length < needed)
        {
            throw new ArgumentException("Destination is too small for the converted frames.", nameof(destination));
        }

        switch (sourceFormat)
        {
            case { IsFloat: true, BitsPerSample: 32 }:
                FloatToInt24(MemoryMarshal.Cast<byte, float>(source), destination);
                break;
            case { IsFloat: true, BitsPerSample: 64 }:
                var doubles = MemoryMarshal.Cast<byte, double>(source);
                for (var i = 0; i < doubles.Length; i++)
                {
                    WriteInt24(destination.Slice(i * 3, 3), FloatToInt24((float)doubles[i]));
                }

                break;
            case { BitsPerSample: 16 }:
            case { BitsPerSample: 24 }:
                source.CopyTo(destination);
                break;
            case { BitsPerSample: 32 }:
                // 24-in-32 (left-justified) or full 32-bit: keep the top 24 bits.
                for (var i = 0; i < samples; i++)
                {
                    var v = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(i * 4, 4));
                    WriteInt24(destination.Slice(i * 3, 3), v >> 8);
                }

                break;
            case { BitsPerSample: 8 }:
                for (var i = 0; i < samples; i++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(i * 2, 2), (short)((source[i] - 128) << 8));
                }

                break;
            default:
                throw new NotSupportedException($"Cannot store {sourceFormat} samples.");
        }

        return needed;
    }

    /// <summary>Decodes interleaved samples of any supported format to floats in [-1, 1].</summary>
    public static void ToFloat(ReadOnlySpan<byte> source, AudioFormat format, Span<float> destination)
    {
        ArgumentNullException.ThrowIfNull(format);
        var samples = source.Length / format.BytesPerSample;
        if (destination.Length < samples)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        switch (format)
        {
            case { IsFloat: true, BitsPerSample: 32 }:
                MemoryMarshal.Cast<byte, float>(source[..(samples * 4)]).CopyTo(destination);
                break;
            case { IsFloat: true, BitsPerSample: 64 }:
                var doubles = MemoryMarshal.Cast<byte, double>(source[..(samples * 8)]);
                for (var i = 0; i < samples; i++)
                {
                    destination[i] = (float)doubles[i];
                }

                break;
            case { BitsPerSample: 16 }:
                for (var i = 0; i < samples; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(i * 2, 2)) / 32768f;
                }

                break;
            case { BitsPerSample: 24 }:
                for (var i = 0; i < samples; i++)
                {
                    destination[i] = ReadInt24(source.Slice(i * 3, 3)) / 8388608f;
                }

                break;
            case { BitsPerSample: 32 }:
                for (var i = 0; i < samples; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(i * 4, 4)) / 2147483648f;
                }

                break;
            case { BitsPerSample: 8 }:
                for (var i = 0; i < samples; i++)
                {
                    destination[i] = (source[i] - 128) / 128f;
                }

                break;
            default:
                throw new NotSupportedException($"Cannot decode {format} samples.");
        }
    }
}
