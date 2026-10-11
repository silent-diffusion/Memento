using Memento.Transcription.Audio;

namespace Memento.Transcription.Tests;

public sealed class SpeechEnergyTests
{
    private const int Rate = 16_000;

    private static float[] Tone(double seconds, double amplitude) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * 220 * i / Rate))).ToArray();

    private static float[] Noise(double seconds, double amplitude, int seed = 1)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, (int)(seconds * Rate)).Select(_ => (float)(amplitude * ((random.NextDouble() * 2) - 1))).ToArray();
    }

    [Fact]
    public void BurstsAboveTheNoiseFloorAreSpeechRegions()
    {
        var energy = new SpeechEnergy();
        energy.Add(Noise(2, 0.001));
        energy.Add(Tone(3, 0.2));
        energy.Add(Noise(4, 0.001, 2));
        energy.Add(Tone(1, 0.2));
        energy.Add(Noise(1, 0.001, 3));

        var regions = energy.Regions();

        Assert.Equal(2, regions.Count);
        Assert.InRange(regions[0].Start, 1.9, 2.1);
        Assert.InRange(regions[0].End, 4.9, 5.1);
        Assert.InRange(regions[1].Start, 8.9, 9.1);
        Assert.False(SpeechEnergy.IsSilent(regions));
        Assert.Equal(11, energy.DurationSeconds, 3);
        Assert.True(SpeechEnergy.HasSpeech(regions, 3, 4));
        Assert.False(SpeechEnergy.HasSpeech(regions, 6, 8));
    }

    [Fact]
    public void ShortPausesAreJoinedAndBlipsDropped()
    {
        var energy = new SpeechEnergy();
        energy.Add(Tone(1, 0.2));
        energy.Add(new float[(int)(0.3 * Rate)]);
        energy.Add(Tone(1, 0.2));
        energy.Add(new float[Rate * 2]);
        energy.Add(Tone(0.1, 0.5));
        energy.Add(new float[Rate * 2]);

        var region = Assert.Single(energy.Regions());

        Assert.InRange(region.End - region.Start, 2.2, 2.4);
    }

    [Fact]
    public void QuietSpeechOverRoomNoiseIsFoundRelativeToTheTracksOwnNoiseFloor()
    {
        // ENGINE-NOTES.md §Q: a quiet microphone, speech about 8 dB over room noise at -60 dBFS. The fixed -40 dBFS bar
        // found nothing here and the whole track was skipped as silent.
        var energy = new SpeechEnergy();
        for (var i = 0; i < 6; i++)
        {
            energy.Add(Noise(3, 0.0017, 10 + i)); // about -60 dBFS RMS
            energy.Add(Mixed(Noise(1.5, 0.0017, 20 + i), Tone(1.5, 0.0035))); // the tone alone about -52 dBFS
        }

        energy.Add(Noise(3, 0.0017, 99));
        var regions = energy.Regions();
        var sound = energy.SoundRegions();

        Assert.Equal(6, regions.Count);
        Assert.False(SpeechEnergy.IsSilent(regions));
        Assert.All(regions.Select((r, i) => (r, i)), x => Assert.InRange(x.r.Start, (4.5 * x.i) + 2.9, (4.5 * x.i) + 3.1));
        Assert.Equal(6, sound.Count);
        var (speechBar, soundBar) = energy.Thresholds();
        Assert.InRange(20 * Math.Log10(speechBar), -58, -52);
        Assert.InRange(20 * Math.Log10(soundBar), -60, -54);
    }

    [Fact]
    public void ALoudCleanTrackKeepsTheFixedBars()
    {
        // Speech at -20 dBFS or louder with digital silence between: the 1.3.0 bars, so line times (ENGINE-NOTES.md §K) are unchanged.
        var energy = new SpeechEnergy();
        energy.Add(new float[Rate]);
        energy.Add(Tone(1, 0.2));
        energy.Add(Tone(1, 0.05)); // speech rises and falls: about -17 and -29 dBFS
        energy.Add(Tone(1, 0.2));
        energy.Add(new float[Rate * 2]);

        var (speechBar, soundBar) = energy.Thresholds();

        Assert.Equal(SpeechEnergy.AbsoluteFloor, speechBar, 6);
        Assert.Equal(SpeechEnergy.SoundFloor, soundBar, 6);
    }

    [Fact]
    public void SteadyNoiseAloneIsSilentWhateverItsLevel()
    {
        foreach (var amplitude in new[] { 0.0005, 0.005, 0.05 })
        {
            var energy = new SpeechEnergy();
            energy.Add(Noise(30, amplitude, 7));
            Assert.True(SpeechEnergy.IsSilent(energy.Regions()), $"noise at amplitude {amplitude}");
        }
    }

    [Fact]
    public void NothingUnderSeventyDecibelsBelowFullScaleIsSpeech()
    {
        var energy = new SpeechEnergy();
        energy.Add(new float[Rate * 5]);
        energy.Add(Tone(2, 0.0002)); // about -77 dBFS
        energy.Add(new float[Rate * 5]);

        Assert.Empty(energy.Regions());
        Assert.Empty(energy.SoundRegions());
    }

    private static float[] Mixed(float[] a, float[] b) => a.Zip(b, (x, y) => x + y).ToArray();

    [Fact]
    public void ADigitallySilentOrQuietTrackIsSilent()
    {
        var silent = new SpeechEnergy();
        silent.Add(new float[Rate * 60]);
        var quiet = new SpeechEnergy();
        quiet.Add(Noise(60, 0.004));

        Assert.True(SpeechEnergy.IsSilent(silent.Regions()));
        Assert.Equal(0, silent.Rms);
        Assert.True(SpeechEnergy.IsSilent(quiet.Regions()));
    }
}
