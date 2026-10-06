using NAudio.Wave;

namespace Memento.Audio.Codecs;

/// <summary>A decoded stream of interleaved float32 samples in [-1, 1]. Dispose to close the file.</summary>
public sealed class DecodedAudio : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _samples;
    private readonly IDisposable? _owner;

    internal DecodedAudio(ISampleProvider samples, IDisposable? owner, TimeSpan? duration)
    {
        _samples = samples;
        _owner = owner;
        Duration = duration;
    }

    public WaveFormat WaveFormat => _samples.WaveFormat;

    public int SampleRate => _samples.WaveFormat.SampleRate;

    public int Channels => _samples.WaveFormat.Channels;

    /// <summary>Duration reported by the container (before any resampling), if known.</summary>
    public TimeSpan? Duration { get; }

    public int Read(float[] buffer, int offset, int count) => _samples.Read(buffer, offset, count);

    /// <summary>Reads everything that is left (for short files: tests, previews, live-transcript windows).</summary>
    public float[] ReadAll()
    {
        var all = new List<float>();
        var buffer = new float[SampleRate * Channels];
        int n;
        while ((n = Read(buffer, 0, buffer.Length)) > 0)
        {
            all.AddRange(buffer.AsSpan(0, n));
        }

        return [.. all];
    }

    public void Dispose() => _owner?.Dispose();
}
