using Memento.Core.Audio;

namespace Memento.Core.Tests.Audio;

/// <summary>
/// The speech level, the per-window normalizer the engines hear through, and the mix's evened levels
/// (ENGINE-NOTES.md §Q).
/// </summary>
public sealed class LevelsTests
{
    private const int Rate = 16_000;

    private static float[] Tone(double seconds, double amplitude) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * 220 * i / Rate))).ToArray();

    private static float[] Noise(double seconds, double amplitude, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, (int)(seconds * Rate)).Select(_ => (float)(amplitude * ((random.NextDouble() * 2) - 1))).ToArray();
    }

    /// <summary>Speech bursts (a tone) over steady noise, as a quiet microphone in a room records them.</summary>
    private static float[] QuietMicrophone(double toneAmplitude, double noiseAmplitude)
    {
        var samples = new List<float>();
        for (var i = 0; i < 5; i++)
        {
            samples.AddRange(Noise(2, noiseAmplitude, i));
            samples.AddRange(Tone(1, toneAmplitude).Zip(Noise(1, noiseAmplitude, 50 + i), (a, b) => a + b));
        }

        return [.. samples];
    }

    private static double Db(double linear) => 20 * Math.Log10(linear);

    [Fact]
    public void TheActiveLevelIsTheSpeechNotThePauses()
    {
        // A -23 dBFS tone a third of the time over -60 dBFS noise: the level is the tone's, not the average.
        var level = SpeechLevel.Measure(QuietMicrophone(0.1, 0.0017), Rate);

        Assert.True(level.HasSpeech);
        Assert.InRange(level.ActiveDbfs, -24, -22);
        Assert.InRange(level.NoiseDbfs, -61.5, -59);
        Assert.InRange(level.ActiveSeconds, 4.8, 5.2);
        Assert.Equal(15, level.Seconds, 3);
    }

    [Fact]
    public void SteadyNoiseOrDigitalSilenceHasNoSpeech()
    {
        Assert.False(SpeechLevel.Measure(Noise(10, 0.01, 1), Rate).HasSpeech);
        var silence = SpeechLevel.Measure(new float[Rate * 10], Rate);
        Assert.False(silence.HasSpeech);
        Assert.Equal(0, silence.NoiseRms);
    }

    [Fact]
    public void TheMeterGivesTheSameLevelBlockByBlockAndInterleaved()
    {
        var mono = QuietMicrophone(0.02, 0.0017);
        var whole = SpeechLevel.Measure(mono, Rate);
        var meter = new SpeechLevelMeter(Rate);
        for (var at = 0; at < mono.Length; at += 777)
        {
            meter.Add(mono.AsSpan(at, Math.Min(777, mono.Length - at)));
        }

        var stereo = new SpeechLevelMeter(Rate);
        stereo.AddInterleaved(mono.SelectMany(s => new[] { s, s }).ToArray(), 2);

        Assert.Equal(whole, meter.Result());
        Assert.Equal(whole.ActiveRms, stereo.Result().ActiveRms, 6);
    }

    [Fact]
    public void TheNormalizerLiftsQuietSpeechToTheTarget()
    {
        var samples = QuietMicrophone(0.0035, 0.0017); // speech about -51 dBFS, about 8 dB over the noise

        var gain = LoudnessNormalizer.Normalize(samples, Rate);

        Assert.InRange(Db(gain), 29, 30.01);
        var after = SpeechLevel.Measure(samples, Rate);
        Assert.InRange(after.ActiveDbfs, LoudnessNormalizer.TargetDbfs - 2, LoudnessNormalizer.TargetDbfs + 1);
    }

    [Fact]
    public void TheNormalizerLowersVeryLoudSpeechButNeverClips()
    {
        var samples = QuietMicrophone(0.9, 0.0017); // speech about -4 dBFS
        var gain = LoudnessNormalizer.Normalize(samples, Rate);

        Assert.Equal(LoudnessNormalizer.MinGainDb, Db(gain), 6);
        Assert.All(samples, s => Assert.InRange(s, -1f, 1f));

        var hot = QuietMicrophone(0.05, 0.0017).Select((s, i) => i % 16_000 == 100 ? 0.9f : s).ToArray(); // -29 dBFS with clicks at 0.9
        Assert.InRange(Db(LoudnessNormalizer.Normalize(hot, Rate)), 8, 10);
        Assert.All(hot, s => Assert.InRange(Math.Abs(s), 0f, 0.99999f));
        Assert.Contains(hot, s => Math.Abs(s) > LoudnessNormalizer.LimiterThreshold);
    }

    [Fact]
    public void TheLimiterIsLinearUpToItsKneeAndNeverReachesFullScale()
    {
        Assert.Equal(0.5f, LoudnessNormalizer.Limit(0.5f));
        Assert.Equal(-0.9f, LoudnessNormalizer.Limit(-0.9f));
        Assert.InRange(LoudnessNormalizer.Limit(1.5f), 0.95f, 0.99999f);
        Assert.InRange(LoudnessNormalizer.Limit(-40f), -0.99999f, -0.99f);
    }

    [Fact]
    public void SilenceAndNoiseStaySilent()
    {
        var silence = new float[Rate * 5];
        Assert.Equal(1, LoudnessNormalizer.Normalize(silence, Rate));
        Assert.All(silence, s => Assert.Equal(0f, s));

        var noise = Noise(5, 0.0017, 3);
        var copy = noise.ToArray();
        Assert.Equal(1, LoudnessNormalizer.Normalize(noise, Rate));
        Assert.Equal(copy, noise);
    }

    [Fact]
    public void TheLiftIsCappedSoAFaintTrackIsNotBlownUp()
    {
        Assert.Equal(LoudnessNormalizer.MaxGainDb, Db(LoudnessNormalizer.GainFor(new SpeechLevel(SpeechLevel.FromDbfs(-65), SpeechLevel.FromDbfs(-75), 10, 60))), 6);
        Assert.Equal(1, LoudnessNormalizer.GainFor(SpeechLevel.None));
    }

    [Fact]
    public void EvenLiftsOnlyTracksWellUnderTheLoudestAndNeverLowersOne()
    {
        static SpeechLevel At(double db) => new(SpeechLevel.FromDbfs(db), SpeechLevel.FromDbfs(db - 30), 20, 60);

        var gains = MixLevels.Even([("system", At(-29.8)), ("mic", At(-53.9)), ("headset", At(-31.5)), ("app", SpeechLevel.None)]);

        Assert.Equal(["system", "mic", "headset", "app"], gains.Select(g => g.TrackId));
        Assert.Equal(0, gains[0].GainDb);
        Assert.Equal(MixLevels.MaxLiftDb, gains[1].GainDb); // 24.1 dB under: capped at 24
        Assert.Equal(0, gains[2].GainDb); // within 3 dB
        Assert.Equal(0, gains[3].GainDb); // no speech: its noise is not lifted
        Assert.Null(gains[3].LevelDbfs);
        Assert.Equal(-53.9, gains[1].LevelDbfs);

        var close = MixLevels.Even([("a", At(-20)), ("b", At(-30))]);
        Assert.Equal(10, close[1].GainDb);
        Assert.All(MixLevels.Even([("solo", At(-50))]), g => Assert.Equal(0, g.GainDb));
    }

    [Fact]
    public void TheHistoryLineNamesWhatWasLifted()
    {
        var text = MixLevels.Describe(
            [new MixTrackGain("system", -29.8, 0), new MixTrackGain("mic", -53.9, 24)],
            id => id == "mic" ? "Microphone" : "System audio");

        Assert.Equal("levels evened: Microphone lifted 24.0 dB (speech at -53.9 dBFS) to match System audio (-29.8 dBFS)", text.Replace('−', '-'));
        Assert.StartsWith("levels evened: every track", MixLevels.Describe([new MixTrackGain("a", -20, 0)], id => id), StringComparison.Ordinal);
    }
}
