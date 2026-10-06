using System.Security.Cryptography;
using Memento.Audio.Codecs;
using Memento.Audio.Writing;
using Memento.Core.Host;
using NAudio.Wave;

namespace Memento.Audio.Tests.Codecs;

public sealed class FlacEncoderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [MediaFoundationFact]
    public async Task TenSecondsOfInt24RoundTripBitExact()
    {
        var pcm = Signals.Int24Sweep(48_000, 2, 10);
        Signals.WriteWav(_dir.File("mic.wav"), AudioFormat.Pcm24(48_000, 2), pcm);
        var track = WavTrackSet.Open(_dir.Path, "mic");
        var output = _dir.File("mic.flac");

        var result = await new MediaFoundationFlacEncoder().EncodeAsync(track, output, CancellationToken.None);

        Assert.Equal(output, result.OutputPath);
        Assert.True(result.Bytes < pcm.Length, "FLAC should be smaller than the WAV");
        Assert.Equal(new FileInfo(output).Length, result.Bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant(), result.Sha256);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Duration);
        Assert.False(File.Exists(output + ".tmp"));

        using var decoded = new MediaFoundationReader(output);
        Assert.Equal(24, decoded.WaveFormat.BitsPerSample);
        Assert.Equal(2, decoded.WaveFormat.Channels);
        Assert.Equal(48_000, decoded.WaveFormat.SampleRate);
        var back = ReadAll(decoded);
        Assert.Equal(pcm.Length, back.Length);
        Assert.True(pcm.AsSpan().SequenceEqual(back), "decoded samples differ from the source");
    }

    [MediaFoundationFact]
    public async Task PartsAreEncodedInSequenceAsOneStream()
    {
        var pcm = Signals.Int24Sweep(48_000, 1, 3);
        using (var writer = new RollingWavWriter(_dir.Path, "system", AudioFormat.Pcm24(48_000, 1), rolloverBytes: 48_000 * 3, durableCheckpoints: false))
        {
            writer.Write(pcm);
        }

        var track = WavTrackSet.Open(_dir.Path, "system");
        Assert.Equal(3, track.Parts.Count);
        var output = _dir.File("system.flac");

        await new MediaFoundationFlacEncoder().EncodeAsync(track, output, CancellationToken.None);

        using var decoded = new MediaFoundationReader(output);
        Assert.True(pcm.AsSpan().SequenceEqual(ReadAll(decoded)));
    }

    [MediaFoundationFact]
    public async Task SixteenBitRoundTripsBitExact()
    {
        var pcm = new byte[44_100 * 2 * 2 * 2];
        new Random(5).NextBytes(pcm);
        Signals.WriteWav(_dir.File("a.wav"), AudioFormat.Pcm16(44_100, 2), pcm);

        await new MediaFoundationFlacEncoder().EncodeAsync(WavTrackSet.Open(_dir.Path, "a"), _dir.File("a.flac"), CancellationToken.None);

        using var decoded = new MediaFoundationReader(_dir.File("a.flac"));
        Assert.True(pcm.AsSpan().SequenceEqual(ReadAll(decoded)));
    }

    [Fact]
    public async Task TooLittleTempSpaceFailsWithTheAmountsAndKeepsTheWav()
    {
        var pcm = Signals.Int24Sweep(48_000, 2, 1);
        var wav = _dir.File("mic.wav");
        Signals.WriteWav(wav, AudioFormat.Pcm24(48_000, 2), pcm);
        var before = File.ReadAllBytes(wav);
        var probe = new FixedFreeSpace(200_000);
        var encoder = new MediaFoundationFlacEncoder(probe, tempDirectory: () => @"T:\Temp\");

        var ex = await Assert.ThrowsAsync<AudioEncodeException>(() => encoder.EncodeAsync(WavTrackSet.Open(_dir.Path, "mic"), _dir.File("mic.flac"), CancellationToken.None));

        Assert.Equal(AudioEncodeErrorCode.InsufficientTempSpace, ex.Code);
        Assert.Equal((long)Math.Ceiling(pcm.Length * 1.1), ex.RequiredBytes);
        Assert.Equal(200_000, ex.AvailableBytes);
        Assert.Contains("mic.wav", ex.Message, StringComparison.Ordinal);
        Assert.Contains("The WAV track is kept", ex.Message, StringComparison.Ordinal);
        Assert.Contains(@"T:\", ex.Message, StringComparison.Ordinal);
        Assert.Equal([@"T:\Temp\"], probe.Queried);
        Assert.False(File.Exists(_dir.File("mic.flac")));
        Assert.Equal(before, File.ReadAllBytes(wav));
    }

    [Fact]
    public void EnoughTempSpacePassesThePreCheck()
    {
        var pcm = Signals.Int24Sweep(48_000, 2, 1);
        Signals.WriteWav(_dir.File("mic.wav"), AudioFormat.Pcm24(48_000, 2), pcm);
        var encoder = new MediaFoundationFlacEncoder(new FixedFreeSpace((long)(pcm.Length * 1.1) + 1));

        encoder.CheckTempSpace(WavTrackSet.Open(_dir.Path, "mic"));
    }

    [Fact]
    public async Task FloatInputIsRejectedBeforeEncoding()
    {
        Signals.WriteWav(_dir.File("f.wav"), AudioFormat.Float32Stereo48k, Signals.AsBytes(Signals.Constant(480, 2, 0.1f)));

        var ex = await Assert.ThrowsAsync<AudioEncodeException>(() => new MediaFoundationFlacEncoder().EncodeAsync(WavTrackSet.Open(_dir.Path, "f"), _dir.File("f.flac"), CancellationToken.None));

        Assert.Equal(AudioEncodeErrorCode.UnsupportedInput, ex.Code);
    }

    [Fact]
    public async Task AnExistingOutputIsNeverOverwritten()
    {
        Signals.WriteWav(_dir.File("m.wav"), AudioFormat.Pcm24(48_000, 2), new byte[600]);
        File.WriteAllBytes(_dir.File("m.flac"), [1, 2, 3]);

        var ex = await Assert.ThrowsAsync<AudioEncodeException>(() => new MediaFoundationFlacEncoder().EncodeAsync(WavTrackSet.Open(_dir.Path, "m"), _dir.File("m.flac"), CancellationToken.None));

        Assert.Equal(AudioEncodeErrorCode.OutputExists, ex.Code);
        Assert.Equal(3, new FileInfo(_dir.File("m.flac")).Length);
    }

    private static byte[] ReadAll(MediaFoundationReader provider)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[65_536];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, n);
        }

        return ms.ToArray();
    }

    private sealed class FixedFreeSpace(long? bytes) : IFreeSpaceProbe
    {
        public List<string> Queried { get; } = [];

        public long? GetFreeBytes(string path)
        {
            Queried.Add(path);
            return bytes;
        }
    }
}
