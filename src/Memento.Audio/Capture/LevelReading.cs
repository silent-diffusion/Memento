namespace Memento.Audio.Capture;

/// <summary>RMS and peak of a stretch of audio, both linear in [0, 1] (full scale = 1).</summary>
public readonly record struct LevelReading(float Rms, float Peak)
{
    public static LevelReading Silence { get; } = new(0, 0);
}
