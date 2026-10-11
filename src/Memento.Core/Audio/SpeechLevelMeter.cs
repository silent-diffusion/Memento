namespace Memento.Core.Audio;

/// <summary>
/// Collects the 100 ms frame RMS values of audio fed block by block, for <see cref="SpeechLevel"/>. Keeps ten floats a
/// second (36,000 for an hour), never the audio.
/// </summary>
public sealed class SpeechLevelMeter
{
    private readonly int _sampleRate;
    private readonly int _frameLength;
    private readonly List<float> _frames = [];
    private double _sum;
    private int _count;
    private long _samples;

    public SpeechLevelMeter(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _sampleRate = sampleRate;
        _frameLength = Math.Max(1, (int)(sampleRate * SpeechLevel.FrameSeconds));
    }

    /// <summary>Mono samples.</summary>
    public void Add(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            AddSample(sample);
        }
    }

    /// <summary>Interleaved samples: each frame's channels are averaged first.</summary>
    public void AddInterleaved(ReadOnlySpan<float> samples, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        for (var i = 0; i + channels <= samples.Length; i += channels)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += samples[i + c];
            }

            AddSample(sum / channels);
        }
    }

    /// <summary>The level of everything added so far (a last frame of at least half the length counts).</summary>
    public SpeechLevel Result()
    {
        var frames = _count > 0 && _count >= _frameLength / 2 ? [.. _frames, (float)Math.Sqrt(_sum / _count)] : _frames;
        return SpeechLevel.FromFrames(frames, _samples / (double)_sampleRate);
    }

    private void AddSample(float sample)
    {
        _sum += (double)sample * sample;
        _count++;
        _samples++;
        if (_count == _frameLength)
        {
            _frames.Add((float)Math.Sqrt(_sum / _count));
            _sum = 0;
            _count = 0;
        }
    }
}
