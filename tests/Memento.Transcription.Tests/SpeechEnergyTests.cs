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
