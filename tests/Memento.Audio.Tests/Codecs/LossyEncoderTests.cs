using Memento.Audio.Codecs;
using Memento.Audio.Writing;
using NAudio.Wave;

namespace Memento.Audio.Tests.Codecs;

public sealed class LossyEncoderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [MediaFoundationFact]
    public async Task Mp3DecodesToTheExpectedDuration()
    {
        var track = WriteTone("mix", 48_000, 2, 5);

        var result = await new MediaFoundationLossyEncoder().EncodeAsync(track, _dir.File("mix.mp3"), new LossyEncodeOptions(LossyCodec.Mp3, 128), null, CancellationToken.None);

        Assert.Equal(128, result.BitrateKbps);
        AssertDecodes(result.OutputPath, TimeSpan.FromSeconds(5), expectedChannels: 2);
        Assert.Equal(64, result.Sha256.Length);
    }

    [MediaFoundationFact]
    public async Task AacWithMonoDownmixDecodesToTheExpectedDuration()
    {
        var track = WriteTone("mic", 48_000, 2, 5);

        var result = await new MediaFoundationLossyEncoder().EncodeAsync(track, _dir.File("mic.m4a"), new LossyEncodeOptions(LossyCodec.Aac, 96) { DownmixToMono = true }, null, CancellationToken.None);

        AssertDecodes(result.OutputPath, TimeSpan.FromSeconds(5), expectedChannels: 1);
    }

    [MediaFoundationFact]
    public async Task Mp3FromAFlacFileAndAnUnusualRateIsResampled()
    {
        var track = WriteTone("odd", 32_000, 1, 2);
        await new MediaFoundationFlacEncoder().EncodeAsync(WriteTone("src", 48_000, 2, 3), _dir.File("src.flac"), CancellationToken.None);

        var fromFlac = await new MediaFoundationLossyEncoder().EncodeAsync(_dir.File("src.flac"), _dir.File("src.mp3"), new LossyEncodeOptions(LossyCodec.Mp3, 192), null, CancellationToken.None);
        var resampled = await new MediaFoundationLossyEncoder().EncodeAsync(track, _dir.File("odd.m4a"), new LossyEncodeOptions(LossyCodec.Aac, 128), null, CancellationToken.None);

        AssertDecodes(fromFlac.OutputPath, TimeSpan.FromSeconds(3), expectedChannels: 2);
        AssertDecodes(resampled.OutputPath, TimeSpan.FromSeconds(2), expectedChannels: 1);
    }

    [Theory]
    [InlineData(LossyCodec.Mp3, 64)]
    [InlineData(LossyCodec.Mp3, 384)]
    [InlineData(LossyCodec.Aac, 8)]
    public async Task BitratesOutsideTheSupportedRangeAreRejected(LossyCodec codec, int kbps)
    {
        var track = WriteTone("x", 48_000, 2, 0.1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new MediaFoundationLossyEncoder().EncodeAsync(track, _dir.File("x.out"), new LossyEncodeOptions(codec, kbps), null, CancellationToken.None));
    }

    private WavTrackSet WriteTone(string stem, int rate, int channels, double seconds)
    {
        Signals.WriteFloatWavAsInt24(_dir.File(stem + ".wav"), rate, channels, Signals.Sine(rate, channels, seconds, 440, 0.3));
        return WavTrackSet.Open(_dir.Path, stem);
    }

    private static void AssertDecodes(string path, TimeSpan expected, int expectedChannels)
    {
        using var reader = new MediaFoundationReader(path);
        long bytes = 0;
        var buffer = new byte[65_536];
        int n;
        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            bytes += n;
        }

        var seconds = bytes / (double)reader.WaveFormat.AverageBytesPerSecond;
        Assert.Equal(expectedChannels, reader.WaveFormat.Channels);
        Assert.InRange(seconds, expected.TotalSeconds - 0.05, expected.TotalSeconds + 0.05);
    }
}
