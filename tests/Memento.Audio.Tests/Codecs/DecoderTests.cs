using Memento.Audio.Codecs;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Codecs;

public sealed class DecoderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void WavTracksDecodeToFloatWithoutMediaFoundation()
    {
        Signals.WriteFloatWavAsInt24(_dir.File("t.wav"), 48_000, 2, Signals.Constant(4_800, 2, 0.5f));

        using var audio = MediaFoundationDecoder.Open(_dir.File("t.wav"));
        var samples = audio.ReadAll();

        Assert.Equal(48_000, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(9_600, samples.Length);
        Assert.All(samples, s => Assert.Equal(0.5f, s, 4));
        Assert.Equal(TimeSpan.FromMilliseconds(100), audio.Duration);
    }

    [Fact]
    public void TranscriptionFormatIs16kMono()
    {
        Signals.WriteFloatWavAsInt24(_dir.File("t.wav"), 48_000, 2, Signals.Sine(48_000, 2, 2, 440, 0.5));

        using var audio = MediaFoundationDecoder.OpenForTranscription(_dir.File("t.wav"));
        var samples = audio.ReadAll();

        Assert.Equal(16_000, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
        Assert.InRange(samples.Length, 31_900, 32_100);
        var rms = Math.Sqrt(samples.Skip(1_000).Take(30_000).Average(s => (double)s * s));
        Assert.Equal(0.5 / Math.Sqrt(2), rms, 2);
    }

    [Fact]
    public void MultiPartTracksDecodeAsOneStream()
    {
        using (var writer = new RollingWavWriter(_dir.Path, "p", AudioFormat.Pcm24(16_000, 1), rolloverBytes: 3_000, durableCheckpoints: false))
        {
            var bytes = new byte[16_000 * 3];
            PcmConverter.FloatToInt24(Signals.Constant(16_000, 1, -0.25f), bytes);
            writer.Write(bytes);
        }

        using var audio = MediaFoundationDecoder.Open(WavTrackSet.Open(_dir.Path, "p"));
        var samples = audio.ReadAll();

        Assert.Equal(16_000, samples.Length);
        Assert.All(samples, s => Assert.Equal(-0.25f, s, 4));
    }

    [MediaFoundationFact]
    public async Task FlacDecodesThroughMediaFoundation()
    {
        Signals.WriteFloatWavAsInt24(_dir.File("m.wav"), 48_000, 2, Signals.Constant(48_000, 2, 0.25f));
        await new MediaFoundationFlacEncoder().EncodeAsync(WavTrackSet.Open(_dir.Path, "m"), _dir.File("m.flac"), CancellationToken.None);

        using var audio = MediaFoundationDecoder.Open(_dir.File("m.flac"));
        var samples = audio.ReadAll();

        Assert.Equal(96_000, samples.Length);
        Assert.Equal(0.25f, samples[500], 4);
    }
}
