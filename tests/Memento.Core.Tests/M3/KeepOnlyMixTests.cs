using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Maintenance;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.M3;

/// <summary>
/// "Keep only the mix" (Settings › Recording › Storage and Settings › Storage and history, 2.0): the optimize stage
/// removes the separate track files once the mix is checked, the manifest records it, Review, export and usage follow,
/// and settings saved by 1.x never turn it on by themselves.
/// </summary>
public sealed class KeepOnlyMixTests : IDisposable
{
    private static readonly string[] UnknownRecording = ["20260101-000000-aaaaaa"];

    private readonly M3Host _m3 = new();

    public void Dispose() => _m3.Dispose();

    [Fact]
    public async Task WithTheSettingOnOnlyTheMixIsLeftAfterProcessingAndTheManifestSaysSo()
    {
        await KeepOnlyMixAsync(true);

        var id = await _m3.RecordAsync("Only the mix", 2, Mic, SystemAudio);
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);

        var folder = _m3.Host.Store.GetProjectFolder(id);
        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.NotNull(manifest.MixOnly);
        Assert.Equal(["mic", "system"], manifest.MixOnly.TrackIds);
        Assert.True(manifest.MixOnly.BytesFreed > 0);
        Assert.Equal(2, manifest.Tracks.Count);
        Assert.All(manifest.Tracks, t => Assert.Null(t.Sha256));
        Assert.All(manifest.Tracks, t => Assert.False(File.Exists(Path.Combine(folder, t.File))));
        Assert.True(File.Exists(Path.Combine(folder, manifest.Mix!.File)));
        Assert.Equal([manifest.Mix.File], manifest.Integrity.Files.Keys);
        Assert.Equal(StageStates.Done, Assert.Single(manifest.Stages, s => s.Stage == StageNames.Optimize).State);

        var line = Assert.Single(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Stage == StageNames.Optimize && h.Event == "completed");
        Assert.StartsWith("Kept only the mix · removed 2 separate tracks (", line.Summary, StringComparison.Ordinal);
        Assert.Contains("speakers can no longer be identified per track", line.Detail, StringComparison.Ordinal);
        Assert.Contains("checked against its SHA-256", line.Detail, StringComparison.Ordinal);

        // project.get carries it, strictly.
        var project = await _m3.ResultAsync("project.get", new { recordingId = id });
        var mixOnly = project.GetProperty("mixOnly");
        Assert.Equal("at,tracks,bytesFreed", string.Join(",", mixOnly.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(2, mixOnly.GetProperty("tracks").GetInt32());
        Assert.Equal(manifest.MixOnly.BytesFreed, mixOnly.GetProperty("bytesFreed").GetInt64());

        // Export leaves the tracks out and says why.
        var estimate = await _m3.ResultAsync("export.estimate", new
        {
            recordingId = id,
            selection = new ExportSelection { AudioMixed = new ExportAudioChoice { On = true, Format = "flac" }, Tracks = new ExportAudioChoice { On = true, Format = "flac" } },
        });
        Assert.Contains(estimate.GetProperty("unavailable").EnumerateArray(), u => u.GetProperty("component").GetString() == "tracks" && u.GetProperty("reason").GetString() == "Only the mix was kept");
        Assert.DoesNotContain(estimate.GetProperty("items").EnumerateArray(), i => i.GetProperty("component").GetString() == "tracks");

        // Usage counts it.
        var usage = await _m3.ResultAsync("library.usage", new { });
        Assert.Equal(0, usage.GetProperty("separateTracksBytes").GetInt64());
        Assert.Equal(0, usage.GetProperty("separateTracksRecordings").GetInt32());
        Assert.Equal(1, usage.GetProperty("mixOnlyRecordings").GetInt32());
    }

    [Fact]
    public async Task OffByDefaultEveryTrackIsKeptAndProjectSaysNothingWasRemoved()
    {
        Assert.False(new StorageSettings().KeepOnlyMix);

        var id = await _m3.RecordAsync("Kept", 2, Mic, SystemAudio);
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);

        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Null(manifest.MixOnly);
        Assert.All(manifest.Tracks, t => Assert.NotNull(t.Sha256));
        Assert.DoesNotContain(manifest.Stages, s => s.Stage == StageNames.Optimize);
        Assert.Equal(JsonValueKind.Null, (await _m3.ResultAsync("project.get", new { recordingId = id })).GetProperty("mixOnly").ValueKind);
    }

    [Fact]
    public async Task AMixThatDoesNotMatchItsChecksumKeepsEveryTrack()
    {
        var id = await _m3.RecordAsync("Damaged mix", 2, Mic);
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);
        var folder = _m3.Host.Store.GetProjectFolder(id);
        var mix = Path.Combine(folder, (await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).Mix!.File);
        File.SetAttributes(mix, FileAttributes.Normal);
        await File.AppendAllTextAsync(mix, "x");

        await _m3.ResultAsync("storage.keepOnlyMix", new { recordingIds = new[] { id } });
        await _m3.Get<StorageReclaimService>().WhenIdleAsync().WaitAsync(Patience.Ceiling);

        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Null(manifest.MixOnly);
        Assert.All(manifest.Tracks, t => Assert.True(File.Exists(Path.Combine(folder, t.File))));
        var line = Assert.Single(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Stage == StageNames.Optimize && h.Event == "info");
        Assert.Equal("Kept the separate tracks", line.Summary);
        Assert.Contains("does not match the checksum", line.Detail, StringComparison.Ordinal);
        Assert.Contains("Nothing was removed", line.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingRecordingsKeepOnlyTheirMixAsOneJobThatSaysWhatItFreed()
    {
        var first = await _m3.RecordAsync("First", 2, Mic, SystemAudio);
        var second = await _m3.RecordAsync("Second", 2, Mic);
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);
        var before = await _m3.ResultAsync("library.usage", new { });
        var expected = before.GetProperty("separateTracksBytes").GetInt64();
        Assert.True(expected > 0);
        Assert.Equal(2, before.GetProperty("separateTracksRecordings").GetInt32());

        var jobId = (await _m3.ResultAsync("storage.keepOnlyMix", new { recordingIds = (string[]?)null })).GetProperty("jobId").GetString();
        await _m3.Get<StorageReclaimService>().WhenIdleAsync().WaitAsync(Patience.Ceiling);

        var done = _m3.Sink.Payloads(BridgeEventNames.StorageReclaimProgress).Last();
        Assert.Equal(jobId, done.GetProperty("jobId").GetString());
        Assert.Equal("done", done.GetProperty("state").GetString());
        Assert.Equal(2, done.GetProperty("recordingsDone").GetInt32());
        Assert.StartsWith("2 recordings keep only the mix; ", done.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(expected, (await _m3.Host.Store.LoadAsync(first, CancellationToken.None)).MixOnly!.BytesFreed + (await _m3.Host.Store.LoadAsync(second, CancellationToken.None)).MixOnly!.BytesFreed);
        var after = await _m3.ResultAsync("library.usage", new { });
        Assert.Equal(0, after.GetProperty("separateTracksBytes").GetInt64());
        Assert.Equal(2, after.GetProperty("mixOnlyRecordings").GetInt32());

        // Nothing left to remove, nothing chosen, or an unknown recording: refused, nothing started.
        var nothing = await _m3.ErrorAsync("storage.keepOnlyMix", new { recordingIds = (string[]?)null });
        var none = await _m3.ErrorAsync("storage.keepOnlyMix", new { recordingIds = Array.Empty<string>() });
        var unknown = await _m3.ErrorAsync("storage.keepOnlyMix", new { recordingIds = UnknownRecording });
        var extra = await _m3.ErrorAsync("storage.keepOnlyMix", new { recordingIds = (string[]?)null, codec = "aac" });
        Assert.Equal(DomainErrorCodes.StorageNothingToReclaim, nothing.GetProperty("code").GetString());
        Assert.Equal("Every recording already keeps only its mix, so there are no separate tracks to remove. Nothing was changed.", nothing.GetProperty("message").GetString());
        Assert.Equal(DomainErrorCodes.StorageNothingToReclaim, none.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, unknown.GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, extra.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheTracksAreRemovedOnceAndRunningAgainChangesNothing()
    {
        await KeepOnlyMixAsync(true);
        var id = await _m3.RecordAsync("Twice", 2, Mic);
        await _m3.Host.Processing.WhenIdleAsync().WaitAsync(Patience.Ceiling);
        var first = (await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).MixOnly;

        // Queued again (a launch that finds the stage queued): it stays done and writes no second line.
        var stage = _m3.Get<Memento.Core.Processing.OptimizeStage>();
        await stage.KeepOnlyTheMixAsync(id, CancellationToken.None);

        var again = (await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).MixOnly!;
        Assert.Equal((first!.At, first.BytesFreed), (again.At, again.BytesFreed));
        Assert.Equal(first.TrackIds, again.TrackIds);
        Assert.Single(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Stage == StageNames.Optimize && h.Event == "completed");
    }

    [Fact]
    public async Task SettingsSavedBy1xNeverTurnOnKeepOnlyTheMixOrTheLiveTranscript()
    {
        var path = _m3.Directory.File("old-settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"recording":{"storage":{"codec":"flac","keepOnlyMix":true}},"transcription":{"timing":"during"}}""");
        using var old = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await old.LoadAsync(CancellationToken.None);

        Assert.False(loaded.Recording.Storage.KeepOnlyMix);
        Assert.Equal(TranscriptionSettings.TimingAfter, loaded.Transcription.Timing);

        // Turned on in 2.0 and saved as schema 2, they stay on.
        await old.UpdateAsync(s => s with { Recording = s.Recording with { Storage = s.Recording.Storage with { KeepOnlyMix = true } }, Transcription = s.Transcription with { Timing = TranscriptionSettings.TimingDuring } }, CancellationToken.None);
        using var again = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        var reloaded = await again.LoadAsync(CancellationToken.None);
        Assert.Equal(2, reloaded.SchemaVersion);
        Assert.True(reloaded.Recording.Storage.KeepOnlyMix);
        Assert.Equal(TranscriptionSettings.TimingDuring, reloaded.Transcription.Timing);
    }

    private Task<AppSettings> KeepOnlyMixAsync(bool on) =>
        _m3.Host.Settings.UpdateAsync(s => s with { Recording = s.Recording with { Storage = s.Recording.Storage with { KeepOnlyMix = on } } }, CancellationToken.None);
}
