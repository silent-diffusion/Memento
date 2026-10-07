using System.Text.Json;
using Memento.Audio.Adapters;
using Memento.Audio.Codecs;
using Memento.Audio.Writing;
using Memento.Core;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Host;
using Memento.Core.Import;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Projects;
using Memento.Core.Recording.Simulation;
using Memento.Core.Secrets;
using Memento.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace Memento.Audio.Tests.M3;

/// <summary>
/// M3 import and export through the real Media Foundation decoder and encoders: an MP3 made with the MP3 encoder and
/// a synthetic AVI become stored recordings, and exports to FLAC, WAV and MP3 decode to the right length.
/// </summary>
public sealed class MediaImportExportTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceProvider _services;
    private readonly Sink _sink = new();

    public MediaImportExportTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IBridgeEventSink>(_sink);
        services.AddSingleton<IFreeSpaceProbe, DriveFreeSpaceProbe>();
        services.AddSingleton<ILibraryLocation>(new Location(_dir.File("Library")));
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(_dir.File("settings.json"), sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton<IAppInfo, AppInfo>();
        services.AddSingleton<IExternalLauncher, Launcher>();
        services.AddSingleton(new ModelStoreOptions(_dir.File("models")));
        services.AddMementoBridge();
        services.AddMementoLibrary();
        services.AddSimulatedAudio(new SimulatedEngineOptions { Speed = 0 });
        services.AddMediaFoundationStorage();
        services.AddMediaFoundationMedia();
        services.Replace(ServiceDescriptor.Singleton(new SecretStoreOptions(_dir.File("secrets.bin"))));
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _dir.Dispose();
    }

    [MediaFoundationFact]
    public async Task TheDecoderReadsAnMp3IntoA24BitWav()
    {
        var mp3 = await Mp3Async("tone", seconds: 3);
        var decoder = new MediaFoundationMediaDecoder();

        var probe = await decoder.ProbeAsync(mp3, CancellationToken.None);
        var progress = new List<double>();
        var decoded = await decoder.DecodeToWavAsync(mp3, _dir.File("out"), "imported", new SyncProgress(progress.Add), CancellationToken.None);

        Assert.Equal(48_000, probe.SampleRate);
        Assert.Equal(2, probe.Channels);
        Assert.False(probe.HasVideo);
        Assert.InRange(probe.DurationMs, 2_950, 3_150);
        var part = Assert.Single(decoded.Parts);
        Assert.Equal(_dir.File(Path.Combine("out", "imported.wav")), part);
        var set = WavTrackSet.FromParts([part]);
        Assert.Equal(24, set.Format.BitsPerSample);
        Assert.InRange(decoded.DurationMs, 2_950, 3_150);
        Assert.Equal(1.0, progress[^1]);
    }

    [MediaFoundationFact]
    public async Task AudioBelow44kHzIsResampledSoItIsStoredAsFlac()
    {
        // LibriVox-style speech: 22.05 kHz mono. The Windows FLAC encoder takes 44.1 kHz and up.
        var source = _dir.File("speech.wav");
        Signals.WriteFloatWavAsInt24(source, 22_050, 1, Signals.Sine(22_050, 1, 2, 300, 0.3));
        var imports = _services.GetRequiredService<MediaImportService>();

        var result = await imports.ImportAsync(new LibraryImportMediaParams { Path = source }, CancellationToken.None);
        await imports.WhenIdleAsync();

        var manifest = await _services.GetRequiredService<IProjectStore>().LoadAsync(result.RecordingId!, CancellationToken.None);
        var track = Assert.Single(manifest.Tracks);
        Assert.Equal(("flac", 44_100, 1), (track.Codec, track.SampleRate, track.Channels));
        Assert.Equal("mix.flac", manifest.Mix!.File);
        Assert.InRange(manifest.DurationMs, 1_950, 2_050);
        Assert.Equal((44_100, 44_100, 48_000, 48_000, 44_100, 96_000), (Rate(22_050), Rate(11_025), Rate(16_000), Rate(8_000), Rate(44_100), Rate(96_000)));

        static int Rate(int source) => MediaFoundationMediaDecoder.StoredSampleRate(source);
    }

    [MediaFoundationFact]
    public async Task AFileWindowsCannotReadIsInvalidData()
    {
        var junk = _dir.File("junk.mp3");
        File.WriteAllBytes(junk, Enumerable.Range(0, 4000).Select(i => (byte)(i * 31)).ToArray());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new MediaFoundationMediaDecoder().ProbeAsync(junk, CancellationToken.None));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [MediaFoundationFact]
    public async Task AnMp3IsImportedAndStoredAsVerifiedFlac()
    {
        var mp3 = await Mp3Async("interview", seconds: 3);
        var originalHash = await FileHashes.Sha256Async(mp3, CancellationToken.None);
        var imports = _services.GetRequiredService<MediaImportService>();

        var result = await imports.ImportAsync(new LibraryImportMediaParams { Path = mp3 }, CancellationToken.None);
        await imports.WhenIdleAsync();

        var store = _services.GetRequiredService<IProjectStore>();
        var manifest = await store.LoadAsync(result.RecordingId!, CancellationToken.None);
        var folder = store.GetProjectFolder(manifest.Id);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.Equal("interview", manifest.Details.Title);
        var track = Assert.Single(manifest.Tracks);
        Assert.Equal("tracks/imported.flac", track.File);
        Assert.Equal("flac", track.Codec);
        Assert.Equal("mix.flac", manifest.Mix!.File);
        Assert.InRange(manifest.DurationMs, 2_950, 3_150);
        Assert.True(File.Exists(Path.Combine(folder, "peaks.json")));
        foreach (var (file, sha256) in manifest.Integrity.Files)
        {
            Assert.Equal(sha256, await FileHashes.Sha256Async(Path.Combine(folder, file), CancellationToken.None));
        }

        Assert.Equal(originalHash, await FileHashes.Sha256Async(mp3, CancellationToken.None));
        var history = await store.ReadHistoryAsync(manifest.Id, CancellationToken.None);
        Assert.Contains(history, h => h.Detail?.Contains(originalHash, StringComparison.Ordinal) == true);
        Assert.DoesNotContain(Directory.GetFiles(folder, "*", SearchOption.AllDirectories), f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
    }

    [MediaFoundationFact]
    public async Task AVideoContainerGivesItsAudioWithAWarning()
    {
        var avi = _dir.File("screen recording.avi");
        var samples = Signals.Sine(48_000, 1, 2, 330, 0.3).Select(s => (short)(s * short.MaxValue)).ToArray();
        AviWriter.Write(avi, 48_000, samples);
        MediaProbe probe;
        try
        {
            probe = await new MediaFoundationMediaDecoder().ProbeAsync(avi, CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            // This Windows has no AVI source; nothing to check here.
            return;
        }

        var imports = _services.GetRequiredService<MediaImportService>();
        var result = await imports.ImportAsync(new LibraryImportMediaParams { Path = avi }, CancellationToken.None);
        await imports.WhenIdleAsync();

        Assert.True(probe.HasVideo);
        var store = _services.GetRequiredService<IProjectStore>();
        var manifest = await store.LoadAsync(result.RecordingId!, CancellationToken.None);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.InRange(manifest.DurationMs, 1_900, 2_100);
        Assert.Contains(await store.ReadHistoryAsync(manifest.Id, CancellationToken.None), h => h.Summary == "Only the audio was imported");
    }

    [MediaFoundationFact]
    public async Task ExportsDecodeToTheRecordingsLength()
    {
        var imports = _services.GetRequiredService<MediaImportService>();
        var id = (await imports.ImportAsync(new LibraryImportMediaParams { Path = await Mp3Async("talk", seconds: 2) }, CancellationToken.None)).RecordingId!;
        await imports.WhenIdleAsync();
        var exports = _services.GetRequiredService<ExportService>();
        var destination = _dir.File("Exports");
        var selection = new ExportSelection
        {
            AudioMixed = new ExportAudioChoice { On = true, Format = "mp3", BitrateKbps = 160 },
            Tracks = new ExportAudioChoice { On = true, Format = "wav" },
        };

        await exports.RunAsync(new ExportRunParams { RecordingId = id, Selection = selection, Destination = new ExportDestination { Folder = destination, CreateSubfolder = false } }, CancellationToken.None);
        await exports.WhenIdleAsync();
        await exports.RunAsync(new ExportRunParams { RecordingId = id, Selection = new ExportSelection { AudioMixed = new ExportAudioChoice { On = true, Format = "flac" } }, Destination = new ExportDestination { Folder = destination, CreateSubfolder = false } }, CancellationToken.None);
        await exports.WhenIdleAsync();

        var done = _sink.Events.Where(e => e.Contains("\"export.progress\"", StringComparison.Ordinal)).Select(e => JsonDocument.Parse(e).RootElement.GetProperty("payload")).Where(p => p.GetProperty("state").GetString() != "running").ToList();
        Assert.All(done, p => Assert.Equal("done", p.GetProperty("state").GetString()));
        var files = Directory.GetFiles(destination);
        var mp3 = Assert.Single(files, f => f.EndsWith(".mp3", StringComparison.Ordinal));
        var wav = Assert.Single(files, f => f.EndsWith(".wav", StringComparison.Ordinal));
        var flac = Assert.Single(files, f => f.EndsWith(".flac", StringComparison.Ordinal));
        AssertDecodes(mp3, 2.0, 0.1);
        AssertDecodes(wav, 2.0, 0.1);
        AssertDecodes(flac, 2.0, 0.1);
        Assert.Equal(24, new WaveFileReader(wav).WaveFormat.BitsPerSample);
        var stored = Path.Combine(_services.GetRequiredService<IProjectStore>().GetProjectFolder(id), "mix.flac");
        Assert.Equal(await FileHashes.Sha256Async(stored, CancellationToken.None), await FileHashes.Sha256Async(flac, CancellationToken.None));
    }

    private static void AssertDecodes(string path, double seconds, double tolerance)
    {
        using var reader = new MediaFoundationReader(path);
        long bytes = 0;
        var buffer = new byte[65_536];
        int n;
        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            bytes += n;
        }

        Assert.InRange(bytes / (double)reader.WaveFormat.AverageBytesPerSecond, seconds - tolerance, seconds + tolerance);
    }

    private async Task<string> Mp3Async(string stem, double seconds)
    {
        Signals.WriteFloatWavAsInt24(_dir.File(stem + ".wav"), 48_000, 2, Signals.Sine(48_000, 2, seconds, 440, 0.3));
        var result = await new MediaFoundationLossyEncoder().EncodeAsync(WavTrackSet.Open(_dir.Path, stem), _dir.File(stem + ".mp3"), new LossyEncodeOptions(LossyCodec.Mp3, 192), null, CancellationToken.None);
        File.Delete(_dir.File(stem + ".wav"));
        return result.OutputPath;
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class Location(string root) : ILibraryLocation
    {
        public string Root => root;
    }

    private sealed class AppInfo : IAppInfo
    {
        public string Version => "0.0.0-test";

        public string OsVersion => "Windows";
    }

    private sealed class Launcher : IExternalLauncher
    {
        public bool TryOpen(Uri uri) => true;
    }

    private sealed class Sink : IBridgeEventSink
    {
        private readonly List<string> _events = [];

        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public void Post(string eventJson)
        {
            lock (_events)
            {
                _events.Add(eventJson);
            }
        }
    }
}
