using Memento.Audio.Adapters;
using Memento.Audio.Codecs;
using Memento.Audio.Mixing;
using Memento.Audio.Writing;
using Memento.Core.Audio;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace Memento.Audio.Tests.Adapters;

/// <summary>Finalize with the real Media Foundation FLAC encoder on synthetic tracks, including ones that rolled over.</summary>
public sealed class MediaFoundationTrackFinalizerTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly MediaFoundationTrackFinalizer _finalizer = new(new MediaFoundationFlacEncoder(), TimeProvider.System, NullLogger<MediaFoundationTrackFinalizer>.Instance);

    public MediaFoundationTrackFinalizerTests() => Directory.CreateDirectory(Tracks);

    private string Tracks => _dir.File("tracks");

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_dir.Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        _dir.Dispose();
    }

    [MediaFoundationFact]
    public async Task MultiPartTracksBecomeOneVerifiedFlacWithAMixAndPeaks()
    {
        // 3 s of stereo int24 rolls over every second: mic.wav, mic.part2.wav, mic.part3.wav.
        var mic = Signals.Int24Sweep(48_000, 2, 3);
        using (var writer = new RollingWavWriter(Tracks, "mic", AudioFormat.Pcm24(48_000, 2), rolloverBytes: 48_000 * 6, durableCheckpoints: false))
        {
            writer.Write(mic);
        }

        Signals.WriteWav(Path.Combine(Tracks, "system.wav"), AudioFormat.Pcm24(48_000, 2), Signals.Int24Sweep(48_000, 2, 2, seed: 7));
        var progress = new List<int>();

        var result = await _finalizer.FinalizeAsync(
            new FinalizeRequest(_dir.Path, [new FinalizeTrackInput("mic", "tracks/mic.wav", 0), new FinalizeTrackInput("system", "tracks/system.wav", 500, 2500)], StorageFormat.Lossless),
            new SyncProgress(progress),
            CancellationToken.None);

        Assert.Equal("flac", result.Codec);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Notes);
        Assert.Equal(["tracks/mic.flac", "tracks/system.flac"], result.Tracks.Select(t => t.File));
        Assert.Equal(["tracks/mic.wav", "tracks/mic.part2.wav", "tracks/mic.part3.wav", "tracks/system.wav"], result.ObsoleteCaptureFiles);
        var micTrack = result.Tracks[0];
        Assert.Equal(3000, micTrack.DurationMs);
        Assert.Equal((48_000, 2), (micTrack.SampleRate, micTrack.Channels));
        Assert.Equal(await FileHashes.Sha256Async(_dir.File("tracks/mic.flac"), CancellationToken.None), micTrack.Sha256);
        Assert.Equal(new FileInfo(_dir.File("tracks/mic.flac")).Length, micTrack.SizeBytes);
        using (var decoded = new MediaFoundationReader(_dir.File("tracks/mic.flac")))
        {
            Assert.True(mic.AsSpan().SequenceEqual(ReadAll(decoded)), "the FLAC holds every part, in order, bit for bit");
        }

        // The capture WAVs stay until the caller has saved the manifest.
        Assert.True(File.Exists(_dir.File("tracks/mic.part3.wav")));
        Assert.True(File.GetAttributes(_dir.File("tracks/mic.flac")).HasFlag(FileAttributes.ReadOnly));

        Assert.Equal("mix.flac", result.Mix.File);
        Assert.Equal(3000, result.Mix.DurationMs);
        Assert.Equal(await FileHashes.Sha256Async(_dir.File("mix.flac"), CancellationToken.None), result.Mix.Sha256);
        Assert.Empty(Directory.GetFiles(_dir.Path, "mix*.wav"));

        var peaks = await Memento.Audio.Mixing.PeakBuilder.ReadAsync(_dir.File("peaks.json"), CancellationToken.None);
        Assert.Equal(1, peaks.SchemaVersion);
        Assert.Equal(50, peaks.WindowMs);
        Assert.Equal(60, peaks.Peaks.Count);
        Assert.All(peaks.Peaks, p =>
        {
            Assert.Equal(2, p.Length);
            Assert.InRange(p[0], 0, p[1]);
            Assert.InRange(p[1], 0, 1);
        });
        Assert.Equal(100, progress[^1]);
    }

    [MediaFoundationFact]
    public async Task ATrackTheFlacEncoderCannotTakeStaysWavWithASpecificNote()
    {
        Signals.WriteWav(Path.Combine(Tracks, "mic.wav"), AudioFormat.Pcm24(48_000, 1), Signals.Int24Sweep(48_000, 1, 1));
        Signals.WriteWav(Path.Combine(Tracks, "phone.wav"), AudioFormat.Pcm16(16_000, 1), new byte[16_000 * 2]);

        var result = await _finalizer.FinalizeAsync(
            new FinalizeRequest(_dir.Path, [new FinalizeTrackInput("mic", "tracks/mic.wav", 0), new FinalizeTrackInput("phone", "tracks/phone.wav", 0)], StorageFormat.Lossless),
            null,
            CancellationToken.None);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Kept phone as WAV", warning.Summary);
        Assert.Contains("44.1–192 kHz", warning.Detail, StringComparison.Ordinal);
        Assert.Contains("The WAV track is kept", warning.Detail, StringComparison.Ordinal);
        var phone = result.Tracks.Single(t => t.TrackId == "phone");
        Assert.Equal(("tracks/phone.wav", "wav"), (phone.File, phone.Codec));
        Assert.Equal(await FileHashes.Sha256Async(_dir.File("tracks/phone.wav"), CancellationToken.None), phone.Sha256);
        Assert.Equal(["tracks/mic.wav"], result.ObsoleteCaptureFiles);
        Assert.Equal("mix.flac", result.Mix.File);
    }

    [MediaFoundationFact]
    public async Task RunningAgainAfterAnInterruptionGivesTheSameFiles()
    {
        Signals.WriteWav(Path.Combine(Tracks, "mic.wav"), AudioFormat.Pcm24(48_000, 2), Signals.Int24Sweep(48_000, 2, 1));
        var request = new FinalizeRequest(_dir.Path, [new FinalizeTrackInput("mic", "tracks/mic.wav", 0)], StorageFormat.Lossless);

        var first = await _finalizer.FinalizeAsync(request, null, CancellationToken.None);
        var second = await _finalizer.FinalizeAsync(request, null, CancellationToken.None);

        Assert.Equal(first.Tracks[0].Sha256, second.Tracks[0].Sha256);
        Assert.Equal(first.Mix.DurationMs, second.Mix.DurationMs);
        Assert.Empty(Directory.GetFiles(_dir.Path, "mix*.wav"));
    }

    [MediaFoundationFact]
    public async Task TheLossyEncodersConvertTheStoredFlacAndTheVerifierDecodesIt()
    {
        Signals.WriteWav(Path.Combine(Tracks, "system.wav"), AudioFormat.Pcm24(48_000, 2), Signals.Int24Sweep(48_000, 2, 2));
        var flacEncoder = new MediaFoundationFlacEncoder();
        var lossy = new MediaFoundationLossyEncoder();
        var verifier = new MediaFoundationAudioVerifier();
        await MediaFoundationAudioEncoder.Flac(flacEncoder).EncodeAsync(_dir.File("tracks/system.wav"), _dir.File("tracks/system.flac"), new AudioEncodeOptions(null, false), CancellationToken.None);
        var aac = MediaFoundationAudioEncoder.Aac(lossy);
        Assert.Equal(("aac", ".m4a", false), (aac.Codec, aac.FileExtension, aac.IsLossless));

        await aac.EncodeAsync(_dir.File("tracks/system.flac"), _dir.File("tracks/system.m4a"), new AudioEncodeOptions(160, DownmixMono: true), CancellationToken.None);
        var check = await verifier.VerifyAsync(_dir.File("tracks/system.m4a"), CancellationToken.None);

        Assert.Equal(1, check.Channels);
        Assert.Equal(48_000, check.SampleRate);
        Assert.InRange(check.DurationMs, 1950, 2250);
        Assert.True(new FileInfo(_dir.File("tracks/system.m4a")).Length < new FileInfo(_dir.File("tracks/system.flac")).Length);

        await File.WriteAllTextAsync(_dir.File("broken.m4a"), "not audio");
        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(_dir.File("broken.m4a"), CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => aac.EncodeAsync(_dir.File("broken.m4a"), _dir.File("out.m4a"), new AudioEncodeOptions(160, false), CancellationToken.None));
        Assert.False(File.Exists(_dir.File("out.m4a")));
    }

    private static byte[] ReadAll(WaveStream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class SyncProgress(List<int> values) : IProgress<int>
    {
        public void Report(int value) => values.Add(value);
    }
}
