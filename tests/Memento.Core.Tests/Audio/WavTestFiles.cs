using System.Buffers.Binary;
using Memento.Core.Audio;

namespace Memento.Core.Tests.Audio;

/// <summary>Synthetic WAV fixtures written with <see cref="StreamingWavWriter"/>.</summary>
internal static class WavTestFiles
{
    /// <summary>Writes 16-bit PCM where every sample of frame <c>f</c>, channel <c>c</c> is <paramref name="sample"/>(f, c).</summary>
    public static void Write(string path, PcmFormat format, int frames, Func<int, int, float> sample)
    {
        using var writer = new StreamingWavWriter(path, format);
        writer.Write(Pcm16(format, frames, sample));
    }

    public static byte[] Pcm16(PcmFormat format, int frames, Func<int, int, float> sample)
    {
        var bytes = new byte[frames * format.BlockAlign];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < format.Channels; c++)
            {
                var value = (short)Math.Clamp(MathF.Round(sample(f, c) * short.MaxValue), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(((f * format.Channels) + c) * 2), value);
            }
        }

        return bytes;
    }

    public static float[] ReadAll(string path)
    {
        using var reader = new WavReader(path);
        var samples = new float[reader.TotalFrames * reader.Format.Channels];
        var read = 0;
        while (true)
        {
            var n = reader.ReadFrames(samples.AsSpan(read * reader.Format.Channels));
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return samples;
    }
}
