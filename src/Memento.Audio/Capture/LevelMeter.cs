using System.Runtime.InteropServices;
using Memento.Audio.Writing;

namespace Memento.Audio.Capture;

/// <summary>
/// Per-track level meter. The pump adds every packet; a publisher calls <see cref="Take"/> at its own rate
/// (≤ 30 Hz) and gets RMS over all samples since the previous call and the largest absolute sample.
/// Runs on the consumer side, never on the capture thread.
/// </summary>
public sealed class LevelMeter
{
    private readonly object _sync = new();
    private float[] _scratch = new float[4096];
    private double _sumSquares;
    private long _samples;
    private float _peak;

    /// <summary>Measures one block of float samples (any channel layout; all samples count).</summary>
    public static LevelReading Measure(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return LevelReading.Silence;
        }

        double sum = 0;
        float peak = 0;
        foreach (var s in samples)
        {
            sum += (double)s * s;
            var a = MathF.Abs(s);
            if (a > peak)
            {
                peak = a;
            }
        }

        return new LevelReading(Clamp01((float)Math.Sqrt(sum / samples.Length)), Clamp01(peak));
    }

    /// <summary>Adds a packet in <paramref name="format"/>.</summary>
    public void Add(ReadOnlySpan<byte> data, AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var count = data.Length / format.BytesPerSample;
        lock (_sync)
        {
            ReadOnlySpan<float> samples;
            if (format is { IsFloat: true, BitsPerSample: 32 })
            {
                samples = MemoryMarshal.Cast<byte, float>(data[..(count * 4)]);
            }
            else
            {
                if (_scratch.Length < count)
                {
                    _scratch = new float[count];
                }

                PcmConverter.ToFloat(data, format, _scratch);
                samples = _scratch.AsSpan(0, count);
            }

            foreach (var s in samples)
            {
                _sumSquares += (double)s * s;
                var a = MathF.Abs(s);
                if (a > _peak && !float.IsNaN(a))
                {
                    _peak = a;
                }
            }

            _samples += count;
        }
    }

    /// <summary>Adds digital silence (silent or synthesized packets).</summary>
    public void AddSilence(int frames, int channels)
    {
        lock (_sync)
        {
            _samples += (long)frames * channels;
        }
    }

    /// <summary>Returns the reading since the last call and starts a new interval.</summary>
    public LevelReading Take()
    {
        lock (_sync)
        {
            var reading = _samples == 0
                ? LevelReading.Silence
                : new LevelReading(Clamp01((float)Math.Sqrt(_sumSquares / _samples)), Clamp01(_peak));
            _sumSquares = 0;
            _samples = 0;
            _peak = 0;
            return reading;
        }
    }

    private static float Clamp01(float v) => float.IsNaN(v) ? 0 : Math.Clamp(v, 0f, 1f);
}
