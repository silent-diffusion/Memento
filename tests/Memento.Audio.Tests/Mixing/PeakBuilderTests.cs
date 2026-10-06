using Memento.Audio.Mixing;
using Memento.Audio.Writing;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Memento.Audio.Tests.Mixing;

public sealed class PeakBuilderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SineGivesRmsAndPeakPerFiftyMillisecondWindow()
    {
        var peaks = PeakBuilder.Build(Provider(Signals.Sine(48_000, 2, 1, 1_000, 0.5), 2));

        Assert.Equal(1, peaks.SchemaVersion);
        Assert.Equal(50, peaks.WindowMs);
        Assert.Equal(20, peaks.Peaks.Count);
        Assert.All(peaks.Peaks, p =>
        {
            Assert.Equal(0.354, p[0], 3);
            Assert.Equal(0.5, p[1], 3);
        });
    }

    [Fact]
    public void APartialLastWindowIsIncludedAndSilenceIsZero()
    {
        var samples = new float[(int)(0.125 * 16_000)];
        Array.Fill(samples, 0.2f, 0, 800);

        var peaks = PeakBuilder.Build(Provider(samples, 1, 16_000));

        Assert.Equal(3, peaks.Peaks.Count);
        Assert.Equal([0.2, 0.2], peaks.Peaks[0]);
        Assert.Equal([0.0, 0.0], peaks.Peaks[1]);
        Assert.Equal([0.0, 0.0], peaks.Peaks[2]);
    }

    [Fact]
    public async Task WritesTheCorePeaksJsonShapeAtomically()
    {
        Signals.WriteFloatWavAsInt24(_dir.File("mix.wav"), 48_000, 2, Signals.Constant(4_800, 2, -0.75f));
        var peaks = await PeakBuilder.BuildAsync(WavTrackSet.Open(_dir.Path, "mix"), CancellationToken.None);
        var path = _dir.File("peaks.json");

        await PeakBuilder.WriteAsync(peaks, path, CancellationToken.None);

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("""{"schemaVersion":1,"windowMs":50,"peaks":[[0.75,0.75],[0.75,0.75]]}""", File.ReadAllText(path));
        var back = await PeakBuilder.ReadAsync(path, CancellationToken.None);
        Assert.Equal(2, back.Peaks.Count);
    }

    private static WaveToSampleProvider Provider(float[] samples, int channels, int rate = 48_000)
    {
        var bytes = Signals.AsBytes(samples);
        var raw = new RawSourceWaveStream(bytes, 0, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(rate, channels));
        return new WaveToSampleProvider(raw);
    }
}
