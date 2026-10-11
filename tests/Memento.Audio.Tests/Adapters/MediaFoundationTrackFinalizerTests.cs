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

    /// <summary>
    /// System audio speaking at about -15 dBFS in 0-1, 2-3 and 4-5 s with digital silence between, and a microphone at
    /// about -37 dBFS in 1-2, 3-4 and 5-6 s over -66 dBFS room noise (ENGINE-NOTES.md §Q).
    /// </summary>
    private void WriteLoudAndQuietTracks()
    {
        const int rate = 48_000;
        var system = new float[rate * 6 * 2];
        var mic = new float[rate * 6];
        var random = new Random(3);
        for (var f = 0; f < rate * 6; f++)
        {
            var second = f / rate;
            var tone = Math.Sin(2 * Math.PI * 200 * f / rate);
            system[2 * f] = system[(2 * f) + 1] = second % 2 == 0 ? (float)(0.25 * tone) : 0f;
            mic[f] = (float)((second % 2 == 1 ? 0.02 * tone : 0) + (0.0009 * ((random.NextDouble() * 2) - 1)));
        }

        Signals.WriteFloatWavAsInt24(Path.Combine(Tracks, "system.wav"), rate, 2, system);
        Signals.WriteFloatWavAsInt24(Path.Combine(Tracks, "mic.wav"), rate, 1, mic);
    }

    /// <summary>RMS in dBFS of the decoded file between two times, every channel.</summary>
    private double LevelDb(string relative, double from, double to)
    {
        using var audio = MediaFoundationDecoder.Open(_dir.File(relative));
        var samples = audio.ReadAll();
        var start = (int)(from * audio.SampleRate) * audio.Channels;
        var end = (int)(to * audio.SampleRate) * audio.Channels;
        var sum = 0.0;
        for (var i = start; i < end; i++)
        {
            sum += samples[i] * (double)samples[i];
        }

        return 10 * Math.Log10(sum / (end - start));
    }

    [MediaFoundationFact]
    public async Task EvenLevelsLiftAQuietMicrophoneInTheMixOnlyAndReportTheGains()
    {
        WriteLoudAndQuietTracks();
        var request = new FinalizeRequest(_dir.Path, [new FinalizeTrackInput("system", "tracks/system.wav", 0), new FinalizeTrackInput("mic", "tracks/mic.wav", 0)], StorageFormat.Lossless) { EvenLevels = true };

        var result = await _finalizer.FinalizeAsync(request, null, CancellationToken.None);

        var gains = Assert.IsAssignableFrom<IReadOnlyList<MixTrackGain>>(result.MixGains);
        Assert.Equal(["system", "mic"], gains.Select(g => g.TrackId));
        Assert.Equal(0, gains[0].GainDb);
        Assert.InRange(gains[0].LevelDbfs!.Value, -16, -14);
        Assert.InRange(gains[1].LevelDbfs!.Value, -38, -36);
        Assert.InRange(gains[1].GainDb, 20.5, 23);

        // In the mix the microphone's speech is now within 3 dB of the system audio's; its own track is as recorded.
        Assert.InRange(LevelDb("mix.flac", 1.1, 1.9) - LevelDb("mix.flac", 0.1, 0.9), -3, 3);
        Assert.InRange(LevelDb("tracks/mic.flac", 1.1, 1.9), -38, -36);
        Assert.All(await ReadMixAsync(), s => Assert.InRange(s, -1f, 1f));
    }

    [MediaFoundationFact]
    public async Task WithoutEvenLevelsTheMixSumsTheTracksAsRecorded()
    {
        WriteLoudAndQuietTracks();
        var request = new FinalizeRequest(_dir.Path, [new FinalizeTrackInput("system", "tracks/system.wav", 0), new FinalizeTrackInput("mic", "tracks/mic.wav", 0)], StorageFormat.Lossless);

        var result = await _finalizer.FinalizeAsync(request, null, CancellationToken.None);

        Assert.Null(result.MixGains);
        Assert.InRange(LevelDb("mix.flac", 1.1, 1.9) - LevelDb("mix.flac", 0.1, 0.9), -24, -20);
    }

    [MediaFoundationFact]
    public async Task RebuildingTheMixWritesNewFilesBesideTheMixAndTouchesNothingElse()
    {
        WriteLoudAndQuietTracks();
        var stored = await _finalizer.FinalizeAsync(
            new FinalizeRequest(_dir.Path, [new FinalizeTrackInput("system", "tracks/system.wav", 0), new FinalizeTrackInput("mic", "tracks/mic.wav", 0)], StorageFormat.Lossless),
            null,
            CancellationToken.None);
        var tracks = stored.Tracks.Select(t => new MixRebuildTrack(t.TrackId, t.File, 0, null)).ToList();
        var mixHash = await FileHashes.Sha256Async(_dir.File("mix.flac"), CancellationToken.None);
        var micHash = await FileHashes.Sha256Async(_dir.File("tracks/mic.flac"), CancellationToken.None);

        var evened = await _finalizer.RebuildMixAsync(new MixRebuildRequest(_dir.Path, tracks, EvenLevels: true, "mix.rebuilt"), null, CancellationToken.None);

        Assert.Equal("mix.rebuilt.flac", evened.Mix.File);
        Assert.Equal("mix.rebuilt.peaks.json", evened.PeaksFile);
        Assert.Equal(await FileHashes.Sha256Async(_dir.File("mix.rebuilt.flac"), CancellationToken.None), evened.Mix.Sha256);
        Assert.Equal(6000, evened.Mix.DurationMs);
        Assert.InRange(evened.Gains![1].GainDb, 20.5, 23);
        Assert.InRange(LevelDb("mix.rebuilt.flac", 1.1, 1.9) - LevelDb("mix.rebuilt.flac", 0.1, 0.9), -3, 3);
        Assert.Equal(mixHash, await FileHashes.Sha256Async(_dir.File("mix.flac"), CancellationToken.None));
        Assert.Equal(micHash, await FileHashes.Sha256Async(_dir.File("tracks/mic.flac"), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_dir.Path, "mix.rebuilt*.wav"));

        // As recorded again: the same samples as the first mix (its FLAC is bit-exact to the same sum).
        File.Delete(_dir.File("mix.rebuilt.flac"));
        var plain = await _finalizer.RebuildMixAsync(new MixRebuildRequest(_dir.Path, tracks, EvenLevels: false, "mix.rebuilt"), null, CancellationToken.None);
        Assert.Null(plain.Gains);
        Assert.Equal(LevelDb("mix.flac", 1.1, 1.9), LevelDb("mix.rebuilt.flac", 1.1, 1.9), 3);
    }

    [MediaFoundationFact]
    public async Task ARebuildWithAMissingTrackLeavesNothingBehind()
    {
        WriteLoudAndQuietTracks();
        var tracks = new List<MixRebuildTrack> { new("system", "tracks/system.wav", 0, null), new("gone", "tracks/gone.flac", 0, null) };

        await Assert.ThrowsAsync<FileNotFoundException>(() => _finalizer.RebuildMixAsync(new MixRebuildRequest(_dir.Path, tracks, true, "mix.rebuilt"), null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_dir.Path, "mix.rebuilt*"));
    }

    private async Task<float[]> ReadMixAsync()
    {
        await Task.Yield();
        using var audio = MediaFoundationDecoder.Open(_dir.File("mix.flac"));
        return audio.ReadAll();
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
