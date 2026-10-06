using Memento.Audio.Codecs;
using Memento.Audio.Mixing;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Mixing;

public sealed class TrackMixerTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task StereoAndMonoTracksStayAlignedWhenOneStartsLate()
    {
        var a = Track("mic", 48_000, 2, Signals.Sine(48_000, 2, 2, 1_000, 0.5));
        var b = Track("app-player", 48_000, 1, Signals.Constant(48_000, 1, 0.3f));

        var result = await TrackMixer.MixAsync(
            [MixInput.FromTrack(a, TimeSpan.Zero), MixInput.FromTrack(b, TimeSpan.FromMilliseconds(500))],
            _dir.Path,
            "mix",
            CancellationToken.None);

        Assert.Equal(AudioFormat.Pcm24(48_000, 2), result.Format);
        Assert.Equal(96_000, result.Frames);
        Assert.Equal(TimeSpan.FromSeconds(2), result.Duration);
        Assert.Equal(0, result.LimitedSamples);
        Assert.Equal([_dir.File("mix.wav")], result.Parts);
        Assert.False(File.Exists(_dir.File("mix.partial.wav")));

        var mix = ReadMix();
        var sine = Signals.Sine(48_000, 2, 2, 1_000, 0.5);
        for (var frame = 0; frame < 96_000; frame++)
        {
            var expected = sine[frame * 2] + (frame is >= 24_000 and < 72_000 ? 0.3f : 0f);
            Assert.Equal(expected, mix[frame * 2], 4);
            Assert.Equal(expected, mix[(frame * 2) + 1], 4);
        }
    }

    [Fact]
    public async Task LoudOverlapIsLimitedNeverClipped()
    {
        var a = Track("a", 48_000, 2, Signals.Sine(48_000, 2, 1, 200, 0.8));
        var b = Track("b", 48_000, 2, Signals.Constant(48_000, 2, 0.7f));

        var result = await TrackMixer.MixAsync([MixInput.FromTrack(a, TimeSpan.Zero), MixInput.FromTrack(b, TimeSpan.Zero)], _dir.Path, "mix", CancellationToken.None);

        Assert.Equal(1.5f, result.PeakBeforeLimiter, 2);
        Assert.True(result.LimitedSamples > 0);
        using var reader = WavTrackSet.Open(_dir.Path, "mix").OpenReader();
        var bytes = new byte[48_000 * 6];
        reader.Read(bytes);
        var max = 0;
        for (var i = 0; i < bytes.Length; i += 3)
        {
            max = Math.Max(max, Math.Abs(PcmConverter.ReadInt24(bytes.AsSpan(i, 3))));
        }

        Assert.True(max < PcmConverter.Int24Max, $"max sample {max} reached full scale");
        Assert.True(max > 0.95 * PcmConverter.Int24Max);
    }

    [Fact]
    public async Task DifferentRatesAreResampledOntoTheTimeline()
    {
        var a = Track("mic", 48_000, 2, new float[48_000 * 2]);
        var b = Track("old", 16_000, 1, Signals.Constant(8_000, 1, 0.4f));

        var result = await TrackMixer.MixAsync([MixInput.FromTrack(a, TimeSpan.Zero), MixInput.FromTrack(b, TimeSpan.FromMilliseconds(250))], _dir.Path, "mix", CancellationToken.None);

        Assert.Equal(48_000, result.Format.SampleRate);
        Assert.InRange(result.Frames, 47_990, 48_010);
        var mix = ReadMix();
        var onset = Array.FindIndex(mix, s => s > 0.2f) / 2;
        Assert.InRange(onset, 12_000 - 96, 12_000 + 96); // within 2 ms of 250 ms
        Assert.Equal(0.4f, mix[30_000 * 2], 2);
    }

    [Fact]
    public async Task ATrackThatEndedEarlyStopsAtItsEndTime()
    {
        var a = Track("a", 48_000, 2, new float[96_000 * 2]);
        var b = Track("b", 48_000, 2, Signals.Constant(96_000, 2, 0.25f));

        await TrackMixer.MixAsync([MixInput.FromTrack(a, TimeSpan.Zero), MixInput.FromTrack(b, TimeSpan.Zero, endedAt: TimeSpan.FromSeconds(1))], _dir.Path, "mix", CancellationToken.None);

        var mix = ReadMix();
        Assert.Equal(0.25f, mix[(47_999 * 2) + 1], 4);
        Assert.Equal(0f, mix[48_000 * 2], 4);
        Assert.Equal(96_000 * 2, mix.Length);
    }

    [Fact]
    public async Task AnExistingMixIsNeverOverwritten()
    {
        var a = Track("a", 48_000, 2, new float[960]);
        File.WriteAllBytes(_dir.File("mix.wav"), [1]);

        await Assert.ThrowsAsync<IOException>(() => TrackMixer.MixAsync([MixInput.FromTrack(a, TimeSpan.Zero)], _dir.Path, "mix", CancellationToken.None));
        Assert.Equal(1, new FileInfo(_dir.File("mix.wav")).Length);
    }

    [Fact]
    public void SoftLimiterIsTransparentBelowTheKneeAndBoundedAbove()
    {
        Assert.Equal(0.5f, SoftLimiter.Apply(0.5f, 0.9f));
        Assert.Equal(-0.9f, SoftLimiter.Apply(-0.9f, 0.9f));
        Assert.InRange(SoftLimiter.Apply(1.2f, 0.9f), 0.9f, 1f);
        Assert.InRange(SoftLimiter.Apply(-50f, 0.9f), -1f, -0.99f);
        Assert.True(SoftLimiter.Apply(1.0f, 0.9f) < SoftLimiter.Apply(1.1f, 0.9f));
    }

    private WavTrackSet Track(string stem, int rate, int channels, float[] samples)
    {
        Signals.WriteFloatWavAsInt24(_dir.File(stem + ".wav"), rate, channels, samples);
        return WavTrackSet.Open(_dir.Path, stem);
    }

    private float[] ReadMix()
    {
        using var audio = MediaFoundationDecoder.Open(WavTrackSet.Open(_dir.Path, "mix"));
        return audio.ReadAll();
    }
}
