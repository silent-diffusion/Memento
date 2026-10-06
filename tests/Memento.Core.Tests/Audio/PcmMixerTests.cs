using Memento.Core.Audio;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Audio;

public sealed class PcmMixerTests : IDisposable
{
    private const float Tolerance = 1f / 8192;

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private string Track(string name, PcmFormat format, int frames, Func<int, int, float> sample)
    {
        var path = _directory.File(name);
        WavTestFiles.Write(path, format, frames, sample);
        return path;
    }

    [Fact]
    public void SumsTracksSampleBySample()
    {
        var a = Track("a.wav", PcmFormat.Pcm16(48_000, 1), 4800, (_, _) => 0.25f);
        var b = Track("b.wav", PcmFormat.Pcm16(48_000, 1), 4800, (_, _) => 0.5f);
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([new MixInput(a, 0), new MixInput(b, 0)], output, downmixMono: false, 50, CancellationToken.None);

        Assert.Equal(PcmFormat.Pcm16(48_000, 1), result.Format);
        Assert.Equal(4800, result.Frames);
        Assert.Equal(100, result.DurationMs);
        Assert.Equal(0, result.ClippedSamples);
        Assert.All(WavTestFiles.ReadAll(output), s => Assert.Equal(0.75f, s, Tolerance));
    }

    [Fact]
    public void PlacesATrackAtItsStartOffset()
    {
        var a = Track("a.wav", PcmFormat.Pcm16(48_000, 1), 9600, (_, _) => 0.1f);
        var b = Track("b.wav", PcmFormat.Pcm16(48_000, 1), 4800, (_, _) => 0.2f);
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([new MixInput(a, 0), new MixInput(b, 150)], output, false, 50, CancellationToken.None);

        var mix = WavTestFiles.ReadAll(output);
        Assert.Equal(48_000 * 250 / 1000, result.Frames);
        Assert.Equal(0.1f, mix[7199], Tolerance);
        Assert.Equal(0.3f, mix[7200], Tolerance);
        Assert.Equal(0.3f, mix[9599], Tolerance);
        Assert.Equal(0.2f, mix[9600], Tolerance);
        Assert.Equal(0.2f, mix[^1], Tolerance);
    }

    [Fact]
    public void MonoJoinsStereoOnBothChannels()
    {
        var mic = Track("mic.wav", PcmFormat.Pcm16(48_000, 1), 480, (_, _) => 0.2f);
        var system = Track("system.wav", PcmFormat.Pcm16(48_000, 2), 480, (_, c) => c == 0 ? 0.1f : -0.1f);
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([new MixInput(mic, 0), new MixInput(system, 0)], output, false, 50, CancellationToken.None);

        var mix = WavTestFiles.ReadAll(output);
        Assert.Equal(2, result.Format.Channels);
        Assert.Equal(0.3f, mix[0], Tolerance);
        Assert.Equal(0.1f, mix[1], Tolerance);
    }

    [Fact]
    public void DownmixAveragesChannels()
    {
        var system = Track("system.wav", PcmFormat.Pcm16(48_000, 2), 480, (_, c) => c == 0 ? 0.4f : 0.2f);
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([new MixInput(system, 0)], output, downmixMono: true, 50, CancellationToken.None);

        Assert.Equal(1, result.Format.Channels);
        Assert.All(WavTestFiles.ReadAll(output), s => Assert.Equal(0.3f, s, Tolerance));
    }

    [Fact]
    public void ResamplesToTheHighestRateByLinearInterpolation()
    {
        // 24 kHz ramp 0, 0.01, 0.02, … mixed with a silent 48 kHz track: every other output sample is a midpoint.
        var slow = Track("slow.wav", PcmFormat.Pcm16(24_000, 1), 100, (f, _) => f / 100f);
        var fast = Track("fast.wav", PcmFormat.Pcm16(48_000, 1), 10, (_, _) => 0f);
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([new MixInput(slow, 0), new MixInput(fast, 0)], output, false, 50, CancellationToken.None);

        var mix = WavTestFiles.ReadAll(output);
        Assert.Equal(48_000, result.Format.SampleRate);
        Assert.Equal(200, result.Frames);
        Assert.Equal(0.10f, mix[20], Tolerance);
        Assert.Equal(0.105f, mix[21], Tolerance);
        Assert.Equal(0.11f, mix[22], Tolerance);
    }

    [Fact]
    public void DownsamplingKeepsTheWholeTrack()
    {
        var high = Track("high.wav", PcmFormat.Pcm16(96_000, 1), 96_000, (f, _) => (f % 2 == 0) ? 0.2f : 0.2f);
        var low = Track("low.wav", PcmFormat.Pcm16(48_000, 1), 48_000, (_, _) => 0f);
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([new MixInput(high, 0), new MixInput(low, 0)], output, false, 50, CancellationToken.None);

        Assert.Equal(96_000, result.Format.SampleRate);
        Assert.Equal(1000, result.DurationMs);
    }

    [Fact]
    public void LoudSumsAreLimitedNotWrapped()
    {
        var a = Track("a.wav", PcmFormat.Pcm16(48_000, 1), 480, (_, _) => 0.8f);
        var b = Track("b.wav", PcmFormat.Pcm16(48_000, 1), 480, (_, _) => 0.8f);
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([new MixInput(a, 0), new MixInput(b, 0)], output, false, 50, CancellationToken.None);

        Assert.Equal(480, result.ClippedSamples);
        Assert.All(WavTestFiles.ReadAll(output), s => Assert.InRange(s, 0.9f, 1f));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(-0.9f, -0.9f)]
    public void TheLimiterPassesQuietSamples(float input, float expected) => Assert.Equal(expected, PcmMixer.Limit(input));

    [Fact]
    public void TheLimiterIsMonotonicAndBounded()
    {
        var previous = PcmMixer.Limit(0.9f);
        foreach (var x in new[] { 0.95f, 1f, 1.5f, 2f, 10f })
        {
            var y = PcmMixer.Limit(x);
            Assert.True(y >= previous);
            Assert.True(y <= 1f);
            Assert.Equal(-y, PcmMixer.Limit(-x));
            previous = y;
        }
    }

    [Fact]
    public void NoTracksMakeAnEmptyMix()
    {
        var output = _directory.File("mix.wav");

        var result = PcmMixer.Mix([], output, false, 50, CancellationToken.None);

        Assert.Equal(0, result.Frames);
        Assert.Equal(0, WavInfo.Read(output).DeclaredDataBytes);
        Assert.Empty(result.Peaks.Peaks);
    }
}
