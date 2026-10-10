using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Processing;

/// <summary>
/// The optimize stage end to end through the bridge: finalize stores lossless files (WAV here: Core has no FLAC
/// encoder), then the stage converts them to the lossy choice with a fake encoder whose output is a real WAV, so the
/// fake verifier can decode it.
/// </summary>
public sealed class OptimizeStageTests : IDisposable
{
    private readonly FakeLossyEncoder _aac = new();
    private readonly FakeVerifier _verifier = new();
    private readonly TempDirectory _directory = new();
    private BridgeTestHost? _host;

    public void Dispose()
    {
        _host?.Dispose();
        _directory.Dispose();
    }

    [Fact]
    public async Task ConvertsEveryLosslessFileUpdatesHashesAndRemovesTheOriginalsLast()
    {
        var host = await StartAsync(new StorageSettings { Codec = StorageSettings.Aac, BitrateKbps = 160 });

        var recordingId = await host.RecordAsync("Smaller", 3, Mic, SystemAudio);
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(recordingId, CancellationToken.None);
        var folder = host.Store.GetProjectFolder(recordingId);
        Assert.Equal(["tracks/mic.m4a", "tracks/system.m4a"], manifest.Tracks.Select(t => t.File));
        Assert.All(manifest.Tracks, t => Assert.Equal("aac", t.Codec));
        Assert.Equal("mix.m4a", manifest.Mix!.File);
        Assert.Equal("aac", manifest.Mix.Codec);
        Assert.Equal(["mix.m4a", "tracks/mic.m4a", "tracks/system.m4a"], manifest.Integrity.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (file, sha256) in manifest.Integrity.Files)
        {
            Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, file), CancellationToken.None), sha256);
        }

        Assert.Equal(manifest.Integrity.Files["tracks/mic.m4a"], manifest.Tracks[0].Sha256);
        Assert.Equal(manifest.Integrity.Files["mix.m4a"], manifest.Mix.Sha256);
        Assert.False(File.Exists(Path.Combine(folder, "tracks", "mic.wav")));
        Assert.False(File.Exists(Path.Combine(folder, "tracks", "system.wav")));
        Assert.False(File.Exists(Path.Combine(folder, "mix.wav")));
        Assert.True(File.Exists(Path.Combine(folder, "peaks.json")));
        Assert.All(_aac.Calls, c => Assert.Equal(160, c.Options.BitrateKbps));
        Assert.Equal(3, _verifier.Verified.Count);

        var optimize = Assert.Single(manifest.Stages, s => s.Stage == StageNames.Optimize);
        Assert.Equal(StageStates.Done, optimize.State);
        Assert.Equal(StageNames.Optimize, manifest.Stages[^1].Stage);

        var history = (await host.Store.ReadHistoryAsync(recordingId, CancellationToken.None)).ToList();
        var started = history.FindIndex(h => h.Stage == StageNames.Optimize && h.Event == "started");
        var completed = history.FindIndex(h => h.Stage == StageNames.Optimize && h.Event == "completed");
        Assert.True(started > history.FindIndex(h => h.Stage == StageNames.Stored && h.Event == "completed"));
        Assert.True(completed > started);
        Assert.Equal("AAC 160 kbps", history[started].Detail);
        Assert.StartsWith("Saved smaller files · ", history[completed].Summary, StringComparison.Ordinal);
        Assert.Contains("tracks/mic.m4a", history[completed].Detail, StringComparison.Ordinal);

        // The row reads "Audio only": neither finished stored nor finished optimize is a pill.
        var list = await host.ResultAsync("library.list");
        var row = Assert.Single(list.GetProperty("recordings").EnumerateArray());
        Assert.Empty(row.GetProperty("stages").EnumerateArray());
        Assert.False(row.GetProperty("isProcessing").GetBoolean());
    }

    [Fact]
    public async Task QueuingAConvertedRecordingAgainKeepsItsStageDone()
    {
        var host = await StartAsync(new StorageSettings { Codec = StorageSettings.Aac, BitrateKbps = 160 });
        var recordingId = await host.RecordAsync("Twice", 2, Mic);
        await host.Processing.WhenIdleAsync();
        var calls = _aac.Calls.Count;

        // Recovery queues a recording and the launch resume finds it queued: the stage may run a second time.
        await host.Processing.EnqueueAfterStoredAsync(recordingId, CancellationToken.None);
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(StageStates.Done, Assert.Single(manifest.Stages, s => s.Stage == StageNames.Optimize).State);
        Assert.Equal("tracks/mic.m4a", manifest.Tracks[0].File);
        Assert.Equal(calls, _aac.Calls.Count);
    }

    [Fact]
    public async Task MonoDownmixAppliesToTracksButNotTheMix()
    {
        var host = await StartAsync(new StorageSettings { Codec = StorageSettings.Aac, BitrateKbps = 128, DownmixMono = true });

        var recordingId = await host.RecordAsync("Mono", 2, Mic, SystemAudio);
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(1, manifest.Tracks.Single(t => t.Id == "system").Channels);
        Assert.Equal(2, manifest.Mix!.Channels);
        Assert.True(_aac.Calls.Single(c => c.Source.EndsWith("system.wav", StringComparison.Ordinal)).Options.DownmixMono);
        Assert.False(_aac.Calls.Single(c => c.Source.EndsWith("mix.wav", StringComparison.Ordinal)).Options.DownmixMono);
        var history = await host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == StageNames.Optimize && h.Detail == "AAC 128 kbps, tracks in mono");
    }

    [Fact]
    public async Task AFileThatDoesNotDecodeKeepsEveryOriginal()
    {
        _verifier.FailFor = "system.m4a";
        var host = await StartAsync(new StorageSettings { Codec = StorageSettings.Aac, BitrateKbps = 160 });

        var recordingId = await host.RecordAsync("Broken", 2, Mic, SystemAudio);
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(recordingId, CancellationToken.None);
        var folder = host.Store.GetProjectFolder(recordingId);
        Assert.Equal(["tracks/mic.wav", "tracks/system.wav"], manifest.Tracks.Select(t => t.File));
        Assert.Equal("mix.wav", manifest.Mix!.File);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.Empty(Directory.GetFiles(folder, "*.m4a", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(folder, "tracks", "mic.wav")));
        Assert.Equal(["mix.wav", "tracks/mic.wav", "tracks/system.wav"], manifest.Integrity.Files.Keys.Order(StringComparer.Ordinal));
        var optimize = Assert.Single(manifest.Stages, s => s.Stage == StageNames.Optimize);
        Assert.Equal(StageStates.Failed, optimize.State);
        Assert.Equal("Smaller files failed", optimize.Label);
        var failed = Assert.Single(await host.Store.ReadHistoryAsync(recordingId, CancellationToken.None), h => h.Stage == StageNames.Optimize && h.Event == "failed");
        Assert.Equal("Making smaller files failed", failed.Summary);
        Assert.Contains("system.m4a does not decode", failed.Detail, StringComparison.Ordinal);
        Assert.Contains("nothing was lost", failed.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LosslessSettingsAddNoStage()
    {
        var host = await StartAsync(new StorageSettings());

        var recordingId = await host.RecordAsync("Lossless", 2, Mic);
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.DoesNotContain(manifest.Stages, s => s.Stage == StageNames.Optimize);
        Assert.Empty(_aac.Calls);
        Assert.Equal("tracks/mic.wav", manifest.Tracks[0].File);
    }

    [Fact]
    public async Task KeepOnlyMixWithASmallerFormatConvertsOnlyTheMixThenRemovesTheTracks()
    {
        var host = await StartAsync(new StorageSettings { Codec = StorageSettings.Aac, BitrateKbps = 160, KeepOnlyMix = true });

        var recordingId = await host.RecordAsync("Only the mix", 2, Mic, SystemAudio);
        await host.Processing.WhenIdleAsync();

        var manifest = await host.Store.LoadAsync(recordingId, CancellationToken.None);
        var folder = host.Store.GetProjectFolder(recordingId);
        Assert.Equal("mix.m4a", manifest.Mix!.File);
        Assert.Single(_aac.Calls); // the mix only: tracks about to be removed are not converted first
        Assert.Equal(2, manifest.Tracks.Count);
        Assert.All(manifest.Tracks, t => Assert.False(File.Exists(Path.Combine(folder, t.File))));
        Assert.Equal(["mic", "system"], manifest.MixOnly!.TrackIds);
        Assert.Equal(["mix.m4a"], manifest.Integrity.Files.Keys);
        var history = await host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == StageNames.Optimize && h.Event == "completed" && h.Summary.StartsWith("Saved smaller files", StringComparison.Ordinal));
        Assert.Contains(history, h => h.Stage == StageNames.Optimize && h.Event == "completed" && h.Summary.StartsWith("Kept only the mix", StringComparison.Ordinal));
        Assert.True(host.Settings.Current.Recording.Storage.KeepOnlyMix);
    }

    [Fact]
    public async Task DeletingARecordingStopsItsConversionFirst()
    {
        _aac.Block = new TaskCompletionSource();
        var host = await StartAsync(new StorageSettings { Codec = StorageSettings.Aac, BitrateKbps = 160 });
        var recordingId = await host.RecordAsync("Deleted while converting", 2, Mic);
        await WaitUntilAsync(() => _aac.Started, "the conversion to start");
        Assert.True(host.Processing.IsBusy(recordingId));

        await host.ResultAsync("project.delete", JsonSerializer.Serialize(new { recordingId }));

        Assert.False(Directory.Exists(host.Store.GetProjectFolder(recordingId)));
        Assert.False(host.Processing.IsBusy(recordingId));
        await host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);
    }

    [Fact]
    public async Task WorkInterruptedByClosingResumesAtTheNextLaunch()
    {
        _aac.Block = new TaskCompletionSource();
        var first = await StartAsync(new StorageSettings { Codec = StorageSettings.Aac, BitrateKbps = 160 });
        var recordingId = await first.RecordAsync("Closed while converting", 2, Mic);
        await WaitUntilAsync(() => _aac.Started, "the conversion to start");

        // Memento closes: the stage is interrupted, its partial files removed, and it is queued again.
        await first.Recordings.ShutdownAsync(CancellationToken.None);
        await first.Processing.StopAsync();
        var interrupted = await first.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(StageStates.Queued, Assert.Single(interrupted.Stages, s => s.Stage == StageNames.Optimize).State);
        Assert.Empty(Directory.GetFiles(first.Store.GetProjectFolder(recordingId), "*.m4a", SearchOption.AllDirectories));
        first.DisposeServicesOnly();
        _host = null;

        _aac.Block = null;
        var second = await StartAsync(null);
        Assert.Equal(1, await second.Processing.ResumePendingAsync(CancellationToken.None));
        await second.Processing.WhenIdleAsync();

        var manifest = await second.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal("tracks/mic.m4a", manifest.Tracks[0].File);
        Assert.Equal(StageStates.Done, Assert.Single(manifest.Stages, s => s.Stage == StageNames.Optimize).State);
    }

    private async Task<BridgeTestHost> StartAsync(StorageSettings? storage)
    {
        _host = new BridgeTestHost(directory: _directory, configure: services =>
        {
            services.AddSingleton<IAudioEncoder>(_aac);
            services.AddSingleton<IAudioFileVerifier>(_verifier);
        });
        await _host.Settings.LoadAsync(CancellationToken.None);
        if (storage is not null)
        {
            await _host.Settings.UpdateAsync(s => s with { Recording = s.Recording with { Storage = storage } }, CancellationToken.None);
        }

        return _host;
    }

    /// <summary>"AAC" whose output is the source as WAV (mono when asked), so the verifier can read it back.</summary>
    private sealed class FakeLossyEncoder : IAudioEncoder
    {
        private readonly PassThroughWavEncoder _wav = new();

        public List<(string Source, AudioEncodeOptions Options)> Calls { get; } = [];

        public TaskCompletionSource? Block { get; set; }

        public bool Started { get; private set; }

        public string Codec => StorageSettings.Aac;

        public string FileExtension => ".m4a";

        public bool IsLossless => false;

        public async Task EncodeAsync(string sourceWavPath, string destinationPath, AudioEncodeOptions options, CancellationToken cancellationToken)
        {
            lock (Calls)
            {
                Calls.Add((sourceWavPath, options));
            }

            Started = true;
            if (Block is { } block)
            {
                await File.WriteAllTextAsync(destinationPath, "partial", cancellationToken);
                await block.Task.WaitAsync(cancellationToken);
            }

            await _wav.EncodeAsync(sourceWavPath, destinationPath, options with { BitrateKbps = null }, cancellationToken);
        }
    }

    private sealed class FakeVerifier : IAudioFileVerifier
    {
        public List<string> Verified { get; } = [];

        public string? FailFor { get; set; }

        public Task<AudioFileCheck> VerifyAsync(string path, CancellationToken cancellationToken)
        {
            if (FailFor is { } name && Path.GetFileName(path) == name)
            {
                throw new InvalidDataException($"{name} does not decode (0x80004005: test)");
            }

            lock (Verified)
            {
                Verified.Add(path);
            }

            var info = WavInfo.Read(path);
            return Task.FromResult(new AudioFileCheck(info.Format.SampleRate, info.Format.Channels, info.DurationMs));
        }
    }
}
