namespace Memento.Core.Audio;

/// <summary>
/// Brings speech to one loudness before an engine hears it (ENGINE-NOTES.md §Q): the gain that puts the audio's active
/// speech level (<see cref="SpeechLevel"/>) at <see cref="TargetDbfs"/>, between <see cref="MinGainDb"/> and
/// <see cref="MaxGainDb"/>, then a soft limiter above <see cref="LimiterThreshold"/> so nothing clips. Audio without
/// speech keeps its level (gain 1), so silence and room noise are never lifted on their own. It works on copies of
/// samples already read from disk (a transcription window, a live window, a track for the diarizer); stored tracks are
/// never changed and nothing here runs on a capture thread.
/// </summary>
public static class LoudnessNormalizer
{
    /// <summary>The active speech level the engines hear: −20 dBFS.</summary>
    public const double TargetDbfs = -20;

    /// <summary>The most a quiet track is lifted: +30 dB (a −50 dBFS voice reaches the target).</summary>
    public const double MaxGainDb = 30;

    /// <summary>The most a loud track is lowered: −10 dB.</summary>
    public const double MinGainDb = -10;

    /// <summary>The limiter's knee: samples above this magnitude are bent smoothly towards full scale.</summary>
    public const float LimiterThreshold = 0.9f;

    /// <summary>The linear gain for audio at <paramref name="level"/>: 1 when it has no speech.</summary>
    public static double GainFor(SpeechLevel level)
    {
        if (!level.HasSpeech)
        {
            return 1;
        }

        var db = Math.Clamp(TargetDbfs - level.ActiveDbfs, MinGainDb, MaxGainDb);
        return SpeechLevel.FromDbfs(db);
    }

    /// <summary>The gain in dB, for logs and History (0 when the level has no speech).</summary>
    public static double GainDbFor(SpeechLevel level) => SpeechLevel.ToDbfs(GainFor(level));

    /// <summary>
    /// Normalizes <paramref name="samples"/> (mono) in place by its own level; returns the gain applied.
    /// </summary>
    public static double Normalize(Span<float> samples, int sampleRate)
    {
        var gain = GainFor(SpeechLevel.Measure(samples, sampleRate));
        Apply(samples, gain);
        return gain;
    }

    /// <summary>Multiplies by <paramref name="gain"/> and bends whatever goes over the knee; never exceeds full scale.</summary>
    public static void Apply(Span<float> samples, double gain)
    {
        var g = (float)gain;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = Limit(samples[i] * g);
        }
    }

    /// <summary>Linear up to the knee, then a tanh curve that approaches but never reaches full scale.</summary>
    public static float Limit(float sample)
    {
        var magnitude = MathF.Abs(sample);
        if (magnitude <= LimiterThreshold)
        {
            return sample;
        }

        const float headroom = 1f - LimiterThreshold;
        var bent = LimiterThreshold + (headroom * MathF.Tanh((magnitude - LimiterThreshold) / headroom));
        return MathF.CopySign(MathF.Min(bent, 0.99999f), sample);
    }
}
