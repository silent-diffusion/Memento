using System.Runtime.InteropServices;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests;

/// <summary>Deterministic synthetic test signals.</summary>
internal static class Signals
{
    /// <summary>Interleaved float sine, the same on every channel.</summary>
    public static float[] Sine(int sampleRate, int channels, double seconds, double hz, double amplitude, long startFrame = 0)
    {
        var frames = (int)Math.Round(seconds * sampleRate);
        var data = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(amplitude * Math.Sin(2 * Math.PI * hz * (startFrame + i) / sampleRate));
            for (var c = 0; c < channels; c++)
            {
                data[(i * channels) + c] = v;
            }
        }

        return data;
    }

    /// <summary>Constant value on every sample.</summary>
    public static float[] Constant(int frames, int channels, float value)
    {
        var data = new float[frames * channels];
        Array.Fill(data, value);
        return data;
    }

    public static byte[] AsBytes(float[] samples) => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

    /// <summary>Packed int24 interleaved: a sweep plus seeded noise, so FLAC has real work to do.</summary>
    public static byte[] Int24Sweep(int sampleRate, int channels, double seconds, int seed = 1234)
    {
        var rng = new Random(seed);
        var frames = (int)(seconds * sampleRate);
        var bytes = new byte[frames * channels * 3];
        double phase = 0;
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)sampleRate;
            phase += 2 * Math.PI * (100 + (4000 * t / seconds)) / sampleRate;
            for (var c = 0; c < channels; c++)
            {
                var v = (0.5 * Math.Sin(phase + c)) + (0.03 * ((rng.NextDouble() * 2) - 1));
                var s = (int)Math.Round(v * PcmConverter.Int24Max);
                PcmConverter.WriteInt24(bytes.AsSpan(((i * channels) + c) * 3, 3), s);
            }
        }

        return bytes;
    }

    public static void WriteWav(string path, AudioFormat format, ReadOnlySpan<byte> data)
    {
        using var writer = new StreamingWavWriter(path, format, durableCheckpoints: false);
        writer.Write(data);
    }

    public static void WriteFloatWavAsInt24(string path, int sampleRate, int channels, float[] samples)
    {
        var bytes = new byte[samples.Length * 3];
        PcmConverter.FloatToInt24(samples, bytes);
        WriteWav(path, AudioFormat.Pcm24(sampleRate, channels), bytes);
    }
}
