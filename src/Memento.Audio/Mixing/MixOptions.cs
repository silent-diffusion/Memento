using Memento.Audio.Writing;

namespace Memento.Audio.Mixing;

/// <summary>Options for <see cref="TrackMixer"/>.</summary>
public sealed record MixOptions
{
    public static MixOptions Default { get; } = new();

    /// <summary>Output rate; null uses the highest input rate (other inputs are resampled with WDL).</summary>
    public int? SampleRate { get; init; }

    /// <summary>Soft-limiter knee: samples above this magnitude are bent smoothly so the sum never exceeds full scale.</summary>
    public float LimiterThreshold { get; init; } = 0.9f;

    public long RolloverBytes { get; init; } = RollingWavWriter.DefaultRolloverBytes;
}
