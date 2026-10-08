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
    public async Task TracksAt44And48KilohertzStayOnOneTimelineForMinutes()
    {
        // A 44.1 kHz microphone and a 48 kHz system track, 3.5 minutes each, with a short burst every 30 s (the
        // system's 15 s later): the mix plays for 3.5 minutes at 48 kHz and every burst is where it was recorded.
        const double seconds = 210;
        var mic = Track("mic", 44_100, 1, Bursts(44_100, 1, seconds, first: 10, every: 30));
        var system = Track("system", 48_000, 2, Bursts(48_000, 2, seconds, first: 25, every: 30));

        var result = await TrackMixer.MixAsync([MixInput.FromTrack(mic, TimeSpan.Zero), MixInput.FromTrack(system, TimeSpan.Zero)], _dir.Path, "mix", CancellationToken.None);

        Assert.Equal(48_000, result.Format.SampleRate);
        Assert.InRange(result.Duration.TotalSeconds, seconds - 0.002, seconds + 0.002);
        var mix = ReadMix();
        Assert.InRange(mix.Length, (seconds * 48_000 * 2) - 200, (seconds * 48_000 * 2) + 200);
        var onsets = BurstOnsets(mix, 48_000, 2);
        var expected = Enumerable.Range(0, 7).SelectMany(i => new[] { 10.0 + (30 * i), 25.0 + (30 * i) }).Where(t => t < seconds).Order().ToList();
        Assert.Equal(expected.Count, onsets.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.InRange(onsets[i], expected[i] - 0.002, expected[i] + 0.002);
        }
    }

    /// <summary>Silence with a 50 ms, 1 kHz burst at <paramref name="first"/> s and every <paramref name="every"/> s after.</summary>
    private static float[] Bursts(int rate, int channels, double seconds, double first, double every)
    {
        var frames = (int)(seconds * rate);
        var samples = new float[frames * channels];
        for (var t = first; t < seconds; t += every)
        {
            var at = (int)Math.Round(t * rate);
            for (var f = 0; f < rate / 20 && at + f < frames; f++)
            {
                var v = (float)(0.5 * Math.Sin(2 * Math.PI * 1_000 * f / rate));
                for (var c = 0; c < channels; c++)
                {
                    samples[((at + f) * channels) + c] = v;
                }
            }
        }

        return samples;
    }

    /// <summary>Seconds where a burst starts: the first sample above 0.05 after at least 1 s of near silence.</summary>
    private static List<double> BurstOnsets(float[] interleaved, int rate, int channels)
    {
        var onsets = new List<double>();
        var quiet = rate;
        for (var f = 0; f < interleaved.Length / channels; f++)
        {
            if (Math.Abs(interleaved[f * channels]) > 0.05f)
            {
                if (quiet >= rate)
                {
                    onsets.Add(f / (double)rate);
                }

                quiet = 0;
            }
            else
            {
                quiet++;
            }
        }

        return onsets;
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
