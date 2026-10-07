using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Import;
using Memento.Core.Library;
using Memento.Core.Maintenance;
using Memento.Core.Projects;
using Memento.Core.Tests.Audio;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.M3;

/// <summary><c>library.importMedia</c>, <c>library.usage</c>, <c>library.rebuildIndex</c>, <c>library.move</c>, <c>storage.reclaim</c>.</summary>
public sealed class ImportAndLibraryTests : IDisposable
{
    private static readonly string[] MissingId = ["20260101-000000-aaaaaa"];

    private readonly M3Host _m3 = new(settingsLibrary: true);

    public void Dispose() => _m3.Dispose();

    [Fact]
    public async Task AWavFileBecomesAStoredRecordingWithOneImportedTrack()
    {
        var source = Tone("board_meeting.wav", seconds: 2);
        var modified = new DateTime(2026, 9, 30, 14, 15, 0, DateTimeKind.Local);
        File.SetLastWriteTime(source, modified);
        var originalHash = await FileHashes.Sha256Async(source, CancellationToken.None);

        var result = await _m3.ResultAsync("library.importMedia", new { path = source });
        var id = result.GetProperty("recordingId").GetString()!;
        await _m3.Get<MediaImportService>().WhenIdleAsync();

        Assert.False(result.GetProperty("cancelled").GetBoolean());
        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.Equal("board meeting", manifest.Details.Title);
        Assert.Equal("meeting", manifest.Details.Type);
        Assert.Equal(new DateTimeOffset(modified), manifest.CreatedAt);
        var track = Assert.Single(manifest.Tracks);
        Assert.Equal("imported", track.Id);
        Assert.Equal("board_meeting.wav", track.Name);
        Assert.Equal("imported", track.SourceId);
        Assert.Equal("imported", track.SourceKind);
        Assert.Equal("tracks/imported.flac", track.File);
        Assert.InRange(track.DurationMs, 1990, 2010);
        Assert.NotNull(track.Sha256);
        Assert.NotNull(manifest.Mix);
        Assert.Equal("peaks.json", manifest.Peaks);
        Assert.False(File.Exists(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "tracks", "imported.wav")));
        Assert.Equal(originalHash, await FileHashes.Sha256Async(source, CancellationToken.None));

        var history = await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None);
        var imported = history.Single(h => h.Summary.StartsWith("Imported board_meeting.wav", StringComparison.Ordinal));
        Assert.Contains(originalHash, imported.Detail, StringComparison.Ordinal);
        Assert.Contains("not copied", imported.Detail, StringComparison.Ordinal);
        Assert.Contains(history, h => h.Summary == "Date taken from the file");
        Assert.Contains(history, h => h.Stage == "stored" && h.Event == "completed");
        Assert.DoesNotContain(history, h => h.Summary == "Only the audio was imported");

        var progress = _m3.Sink.Payloads(BridgeEventNames.ProcessingProgress).Where(p => p.GetProperty("recordingId").GetString() == id).ToList();
        Assert.Contains(progress, p => p.GetProperty("stages")[0].GetProperty("label").GetString()!.EndsWith("importing", StringComparison.Ordinal));
        Assert.Equal("done", progress[^1].GetProperty("stages")[0].GetProperty("state").GetString());
        Assert.Single((await _m3.Host.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
    }

    [Fact]
    public async Task TitleAndTypeCanBeGivenAndVideoKeepsOnlyItsAudio()
    {
        var source = Tone("clip.mp4", seconds: 1);

        var id = (await _m3.ResultAsync("library.importMedia", new { path = source, title = "Product demo", type = "presentation" })).GetProperty("recordingId").GetString()!;
        await _m3.Get<MediaImportService>().WhenIdleAsync();

        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Equal("Product demo", manifest.Details.Title);
        Assert.Equal("presentation", manifest.Details.Type);
        Assert.False(manifest.HasVideo);
        var history = await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Only the audio was imported" && h.Event == "info");
    }

    [Fact]
    public async Task AnImportCutShortByACrashIsMarkedFailedAtLaunchAndCanBeImportedAgain()
    {
        var source = Tone("standup.wav", seconds: 2);
        var id = (await _m3.ResultAsync("library.importMedia", new { path = source })).GetProperty("recordingId").GetString()!;
        await _m3.Get<MediaImportService>().WhenIdleAsync();
        var originalHash = await FileHashes.Sha256Async(source, CancellationToken.None);

        // What a crash halfway through decoding leaves: the project still finalizing, a partial WAV in tracks/.
        var folder = _m3.Host.Store.GetProjectFolder(id);
        foreach (var file in Directory.GetFiles(Path.Combine(folder, "tracks")))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        File.WriteAllBytes(Path.Combine(folder, "tracks", "imported.wav"), new byte[4096]);
        await _m3.Get<ProjectCatalog>().UpdateAsync(
            id,
            m => m with { State = ProjectStates.Finalizing, Mix = null, Stages = [new Core.Bridge.Contracts.StageStatus(StageNames.Stored, "active", 40, "40% · importing")] },
            CancellationToken.None);

        var recovered = await _m3.Get<Core.Recovery.RecoveryService>().RunAsync(CancellationToken.None);

        Assert.Empty(recovered);
        var failed = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Equal(ProjectStates.Failed, failed.State);
        var stage = Assert.Single(failed.Stages);
        Assert.Equal((StageNames.Stored, "failed", "Import interrupted"), (stage.Stage, stage.State, stage.Label));
        var failure = Assert.Single(failed.Failures);
        Assert.Equal(["importAgain"], failure.Remedies.Select(r => r.Id));
        Assert.Contains("standup.wav", failure.Message, StringComparison.Ordinal);
        Assert.Contains("The original file was not changed", failure.Kept, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "tracks", "imported.wav")));
        Assert.Contains(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Summary == "Import interrupted" && h.Event == "failed");
        var row = (await _m3.Host.ResultAsync("library.list")).GetProperty("recordings")[0];
        Assert.Equal("failed", row.GetProperty("state").GetString());

        // A second launch leaves it as it is.
        await _m3.Get<Core.Recovery.RecoveryService>().RunAsync(CancellationToken.None);
        Assert.Equal(ProjectStates.Failed, (await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).State);

        // Import again: the same file into the same project, then the normal stages.
        await _m3.ResultAsync("processing.retry", new { recordingId = id, stage = "stored", remedyId = "importAgain" });
        await _m3.Get<MediaImportService>().WhenIdleAsync();

        var again = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.Equal(ProjectStates.Ready, again.State);
        Assert.Empty(again.Failures);
        Assert.Equal("tracks/imported.flac", Assert.Single(again.Tracks).File);
        Assert.NotNull(again.Mix);
        Assert.Equal(source, again.ImportedFrom?.Path);
        Assert.Equal(originalHash, await FileHashes.Sha256Async(source, CancellationToken.None));
        Assert.Contains(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Summary == "Importing again");
    }

    [Fact]
    public async Task ImportAgainIsRefusedForARecordingAndWhenTheFileIsGone()
    {
        var recording = await _m3.RecordAsync();
        var notAnImport = await _m3.ErrorAsync("processing.retry", new { recordingId = recording, stage = "stored" });

        var source = Tone("gone.wav", seconds: 1);
        var id = (await _m3.ResultAsync("library.importMedia", new { path = source })).GetProperty("recordingId").GetString()!;
        await _m3.Get<MediaImportService>().WhenIdleAsync();
        await _m3.Get<ProjectCatalog>().UpdateAsync(id, m => m with { State = ProjectStates.Finalizing }, CancellationToken.None);
        await _m3.Get<Core.Recovery.RecoveryService>().RunAsync(CancellationToken.None);
        File.Delete(source);
        var gone = await _m3.ErrorAsync("processing.retry", new { recordingId = id, stage = "stored", remedyId = "importAgain" });

        Assert.Equal(BridgeErrorCodes.InvalidParams, notAnImport.GetProperty("code").GetString());
        Assert.Contains("is not an import that stopped", notAnImport.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("\"gone.wav\" is no longer at", gone.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(ProjectStates.Failed, (await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task AFileTooBigForTheFreeSpaceIsRefusedBeforeAnythingIsWritten()
    {
        var source = Tone("long_lecture.wav", seconds: 2);
        _m3.Host.FreeSpace.FreeBytes = 512L * 1024 * 1024; // less than the 1 GB margin alone

        var error = await _m3.ErrorAsync("library.importMedia", new { path = source });

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.GetProperty("code").GetString());
        Assert.Contains("needs about", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was imported", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_m3.Host.Store.ListIds());

        _m3.Host.FreeSpace.FreeBytes = null; // unreadable: the import goes ahead
        var result = await _m3.ResultAsync("library.importMedia", new { path = source });
        Assert.NotNull(result.GetProperty("recordingId").GetString());
    }

    [Fact]
    public async Task AFileThatCannotBeDecodedIsRefusedAndNothingIsAdded()
    {
        var source = _m3.WriteFile("noise.mp3", "ID3 not really audio");

        var error = await _m3.ErrorAsync("library.importMedia", new { path = source });
        _m3.Picker.Answer = null;
        var cancelled = await _m3.ResultAsync("library.importMedia", new { });
        var badType = await _m3.ErrorAsync("library.importMedia", new { path = source, type = new string('t', 41) });

        Assert.Equal(DomainErrorCodes.LibraryImportUnsupported, error.GetProperty("code").GetString());
        Assert.Contains("\"noise.mp3\" can't be imported", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was added", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("""{"recordingId":null,"cancelled":true}""", cancelled.GetRawText());
        Assert.Contains(_m3.Picker.Calls[0].Filters, f => f.Patterns.Contains("*.mp4"));
        Assert.Equal(BridgeErrorCodes.InvalidParams, badType.GetProperty("code").GetString());
        Assert.Empty(_m3.Host.Store.ListIds());
    }

    [Fact]
    public async Task UsageCountsTheLibraryAndFindsTheLargestRecording()
    {
        await _m3.RecordAsync("Short", 1);
        var longer = await _m3.RecordAsync("Long", 4);
        _m3.Host.FreeSpace.FreeBytes = 123_456_789;

        var usage = await _m3.ResultAsync("library.usage", new { });

        Assert.Equal("totalBytes,freeBytes,count,largest", string.Join(",", usage.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(2, usage.GetProperty("count").GetInt32());
        Assert.Equal(123_456_789, usage.GetProperty("freeBytes").GetInt64());
        Assert.Equal(longer, usage.GetProperty("largest").GetProperty("recordingId").GetString());
        Assert.Equal("Long", usage.GetProperty("largest").GetProperty("title").GetString());
        Assert.True(usage.GetProperty("totalBytes").GetInt64() > usage.GetProperty("largest").GetProperty("sizeBytes").GetInt64());

        _m3.Sink.Clear();
        var rebuilt = await _m3.ResultAsync("library.rebuildIndex", new { });
        Assert.Equal("""{"recordings":2}""", rebuilt.GetRawText());
        Assert.Equal(2, _m3.Sink.Payloads(BridgeEventNames.LibraryChanged).Single().GetProperty("recordingIds").GetArrayLength());
    }

    [Fact]
    public async Task TheLibraryListsLargestFirstWithSortSize()
    {
        var small = await _m3.RecordAsync("Small", 1);
        var large = await _m3.RecordAsync("Large", 4);

        var list = await _m3.ResultAsync("library.list", new { sort = "size" });

        Assert.Equal([large, small], list.GetProperty("recordings").EnumerateArray().Select(r => r.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task UsageOfAnEmptyLibrary()
    {
        var usage = await _m3.ResultAsync("library.usage", new { });

        Assert.Equal(0, usage.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("largest").ValueKind);
    }

    [Fact]
    public async Task TheLibraryMovesWithEveryFileVerifiedAndBack()
    {
        var id = await _m3.RecordAsync();
        await _m3.ResultAsync("attachments.add", new { recordingId = id, path = _m3.WriteFile("notes.txt", "notes") });
        var original = _m3.Host.Settings.Current.EffectiveLibraryPath;
        var hashes = await HashesAsync(original);
        var target = _m3.Directory.File("Moved library");

        var done = await MoveAsync(target);

        Assert.Equal("done", done.GetProperty("state").GetString());
        Assert.Equal(100, done.GetProperty("percent").GetInt32());
        Assert.Equal(target, done.GetProperty("newPath").GetString());
        Assert.Equal(target, _m3.Host.Settings.Current.LibraryPath);
        Assert.False(Directory.Exists(original));
        Assert.Equal(hashes, await HashesAsync(target));
        Assert.True((File.GetAttributes(Path.Combine(target, "projects", id, "mix.flac")) & FileAttributes.ReadOnly) != 0);
        Assert.Equal("Weekly sync", (await _m3.ResultAsync("project.get", new { recordingId = id })).GetProperty("summary").GetProperty("title").GetString());
        Assert.Single((await _m3.Host.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(target, "library.db")));

        var back = await MoveAsync(original);
        Assert.Equal("done", back.GetProperty("state").GetString());
        Assert.Equal(hashes, await HashesAsync(original));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task TheLibraryIsNotMovedWhileRecordingOrIntoAFolderWithFiles()
    {
        var full = _m3.Directory.File("Full");
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, "other.txt"), "x");
        var root = _m3.Host.Settings.Current.EffectiveLibraryPath;

        var notEmpty = await _m3.ErrorAsync("library.move", new { newPath = full });
        var inside = await _m3.ErrorAsync("library.move", new { newPath = Path.Combine(root, "sub") });
        var same = await _m3.ErrorAsync("library.move", new { newPath = root });
        var relative = await _m3.ErrorAsync("library.move", new { newPath = "Library" });
        var (sessionId, _) = await _m3.Host.StartAsync("Live", TestRecordings.Mic);
        var busy = await _m3.ErrorAsync("library.move", new { newPath = _m3.Directory.File("Elsewhere") });
        await _m3.Host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _m3.Host.Recordings.WhenIdleAsync();

        Assert.All(new[] { notEmpty, inside, same, relative }, e => Assert.Equal(DomainErrorCodes.LibraryMoveRefused, e.GetProperty("code").GetString()));
        Assert.Contains("already has files", notEmpty.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(full, notEmpty.GetProperty("detail").GetString());
        Assert.Equal(DomainErrorCodes.LibraryBusy, busy.GetProperty("code").GetString());
        Assert.Contains("a recording is in progress", busy.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(_m3.Directory.File("Elsewhere")));
        Assert.Equal(root, _m3.Host.Settings.Current.EffectiveLibraryPath);
    }

    [Fact]
    public async Task ARecordingDoesNotStartWhileTheLibraryIsBeingMoved()
    {
        var activity = _m3.Get<LibraryActivity>();
        using (activity.Begin(LibraryActivity.Move))
        {
            var refused = await _m3.ErrorAsync("recording.start", new { title = "Live", type = "meeting", sourceIds = new[] { TestRecordings.Mic } });

            Assert.Equal(DomainErrorCodes.LibraryBusy, refused.GetProperty("code").GetString());
            Assert.Contains("can't start right now. Nothing was started.", refused.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Null(_m3.Host.Recordings.Current);
            Assert.False(activity.IsStartingRecording);
        }

        // Once the move is over, recording works again.
        var (sessionId, _) = await _m3.Host.StartAsync("Live", TestRecordings.Mic);
        Assert.False(activity.IsStartingRecording);
        await _m3.Host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _m3.Host.Recordings.WhenIdleAsync();
    }

    [Fact]
    public async Task TheLibraryIsNotMovedWhileARecordingIsStarting()
    {
        var activity = _m3.Get<LibraryActivity>();
        var target = _m3.Directory.File("Elsewhere");
        using (activity.BeginRecordingStart())
        {
            var busy = await _m3.ErrorAsync("library.move", new { newPath = target });

            Assert.Equal(DomainErrorCodes.LibraryBusy, busy.GetProperty("code").GetString());
            Assert.False(activity.IsMoving);
        }

        Assert.False(Directory.Exists(target));
        Assert.Null(activity.Describe());
    }

    [Fact]
    public void TheIndexIsNotCopiedButRebuilt()
    {
        Assert.True(LibraryMoveService.IsIndexFile("library.db"));
        Assert.True(LibraryMoveService.IsIndexFile("library.db-wal"));
        Assert.False(LibraryMoveService.IsIndexFile("project.json"));
    }

    [Fact]
    public async Task ReclaimRunsTheOptimizeStageWithTheChosenOptionsAndLeavesTheTranscript()
    {
        var id = await _m3.RecordAsync();
        var transcript = _m3.WriteTranscript(id);
        var transcriptBytes = File.ReadAllBytes(transcript);

        var jobId = (await _m3.ResultAsync("storage.reclaim", new { recordingIds = new[] { id }, downmixMono = false, codec = "aac", bitrateKbps = 128 })).GetProperty("jobId").GetString();
        await _m3.Get<StorageReclaimService>().WhenIdleAsync();

        var done = _m3.Sink.Payloads(BridgeEventNames.StorageReclaimProgress).Last();
        Assert.Equal(jobId, done.GetProperty("jobId").GetString());
        Assert.Equal("done", done.GetProperty("state").GetString());
        Assert.Equal(1, done.GetProperty("recordingsDone").GetInt32());
        Assert.Contains("Transcripts were not changed", done.GetProperty("message").GetString(), StringComparison.Ordinal);
        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        Assert.All(manifest.Tracks, t => Assert.Equal("aac", t.Codec));
        Assert.Equal("mix.m4a", manifest.Mix!.File);
        Assert.Equal(transcriptBytes, File.ReadAllBytes(transcript));
        Assert.Contains(await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Stage == StageNames.Optimize && h.Event == "completed" && h.Detail!.Contains("AAC 128 kbps", StringComparison.Ordinal));
        Assert.Equal("flac", _m3.Host.Settings.Current.Recording.Storage.Codec);
    }

    [Fact]
    public async Task ReclaimValidatesItsOptions()
    {
        var id = await _m3.RecordAsync();

        var codec = await _m3.ErrorAsync("storage.reclaim", new { recordingIds = new[] { id }, downmixMono = false, codec = "flac", bitrateKbps = 128 });
        var mp3 = await _m3.ErrorAsync("storage.reclaim", new { recordingIds = new[] { id }, downmixMono = false, codec = "mp3", bitrateKbps = 64 });
        var noAge = await _m3.ErrorAsync("storage.reclaim", new { recordingIds = (string[]?)null, downmixMono = true, codec = "aac", bitrateKbps = (int?)null });
        var unknown = await _m3.ErrorAsync("storage.reclaim", new { recordingIds = MissingId, downmixMono = false, codec = "aac" });
        var none = await _m3.ErrorAsync("storage.reclaim", new { recordingIds = Array.Empty<string>(), downmixMono = false, codec = "aac" });

        Assert.Equal(BridgeErrorCodes.InvalidParams, codec.GetProperty("code").GetString());
        Assert.Contains("96 to 320", mp3.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(DomainErrorCodes.StorageNothingToReclaim, noAge.GetProperty("code").GetString());
        Assert.Contains("Settings › Storage and history", noAge.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(DomainErrorCodes.ProjectNotFound, unknown.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.StorageNothingToReclaim, none.GetProperty("code").GetString());

        // With an age set, only older recordings are chosen: this one was made just now, so nothing is started.
        await _m3.ResultAsync("settings.set", new { storage = new { reclaimOlderThanDays = 30 } });
        var young = await _m3.ErrorAsync("storage.reclaim", new { recordingIds = (string[]?)null, downmixMono = false, codec = "aac" });
        Assert.Equal(DomainErrorCodes.StorageNothingToReclaim, young.GetProperty("code").GetString());
        Assert.Contains("older than 30 days", young.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_m3.Sink.Payloads(BridgeEventNames.StorageReclaimProgress));
        Assert.All((await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).Tracks, t => Assert.Equal("flac", t.Codec));
    }

    private string Tone(string name, double seconds)
    {
        var path = _m3.Directory.File(name);
        var format = PcmFormat.Pcm16(48_000, 2);
        WavTestFiles.Write(path, format, (int)(seconds * 48_000), (f, c) => 0.3f * MathF.Sin(2 * MathF.PI * 440 * f / 48_000));
        return path;
    }

    private async Task<System.Text.Json.JsonElement> MoveAsync(string target)
    {
        var jobId = (await _m3.ResultAsync("library.move", new { newPath = target })).GetProperty("jobId").GetString();
        await _m3.Get<LibraryMoveService>().WhenIdleAsync();
        return _m3.Sink.Payloads(BridgeEventNames.LibraryMoveProgress).Last(p => p.GetProperty("jobId").GetString() == jobId);
    }

    /// <summary>Every project file and its SHA-256, by path relative to the library.</summary>
    private static async Task<Dictionary<string, string>> HashesAsync(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "projects"), "*", SearchOption.AllDirectories))
        {
            result[Path.GetRelativePath(root, file)] = await FileHashes.Sha256Async(file, CancellationToken.None);
        }

        return result;
    }
}
