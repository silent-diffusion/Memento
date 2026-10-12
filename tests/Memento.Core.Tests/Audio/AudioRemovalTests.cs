using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Audio;

/// <summary>2.1: Delete audio (<c>audio.remove</c>, <c>audio.removeEstimate</c>) and Delete audio tracks older than.</summary>
public sealed class AudioRemovalTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string Error(JsonElement response) => response.GetProperty("error").GetProperty("code").GetString()!;

    private async Task<string> RecordAsync(string title)
    {
        var recordingId = await _host.RecordAsync(title, 2, Mic, SystemAudio);
        await _host.Processing.WhenIdleAsync();
        return recordingId;
    }

    private Dictionary<string, long> FilesOf(string recordingId)
    {
        var folder = _host.Store.GetProjectFolder(recordingId);
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), f => new FileInfo(f).Length);
    }

    [Fact]
    public async Task DeletingTheAudioRecordsItFirstThenRemovesOnlyAudioFiles()
    {
        var recordingId = await RecordAsync("Budget review");
        var folder = _host.Store.GetProjectFolder(recordingId);
        await File.WriteAllTextAsync(Path.Combine(folder, "transcript.json"), "{}");
        await File.WriteAllBytesAsync(Path.Combine(folder, "tracks", "screen.mp4"), new byte[50]);
        Directory.CreateDirectory(Path.Combine(folder, "documents"));
        await File.WriteAllTextAsync(Path.Combine(folder, "documents", "d1.json"), "{}");
        await File.WriteAllBytesAsync(Path.Combine(folder, "tracks", "mic.part2.wav"), new byte[30]);
        var before = FilesOf(recordingId);
        var audio = AudioRemovalService.AudioFiles(await _host.Store.LoadAsync(recordingId, CancellationToken.None), folder);
        Assert.Contains(audio, f => f.File == "mix.flac" || f.File.StartsWith("mix.", StringComparison.Ordinal));
        Assert.Contains(audio, f => f.File == "peaks.json");
        Assert.Contains(audio, f => f.File == "tracks/mic.part2.wav");
        Assert.DoesNotContain(audio, f => f.File.EndsWith(".mp4", StringComparison.Ordinal) || f.File.EndsWith("transcript.json", StringComparison.Ordinal));

        // The manifest says it before any file goes: at the first write, every audio file is still there.
        var manifestFirst = (bool?)null;
        _host.Sink.Posting = json =>
        {
            if (manifestFirst is null && json.Contains("\"library.changed\"", StringComparison.Ordinal))
            {
                var written = _host.Store.LoadAsync(recordingId, CancellationToken.None).GetAwaiter().GetResult();
                manifestFirst = written.AudioRemoved is not null && audio.All(f => File.Exists(ProjectPaths.Resolve(folder, f.File)));
            }
        };
        var result = await _host.ResultAsync("audio.remove", JsonSerializer.Serialize(new { recordingId }));
        _host.Sink.Posting = null;

        Assert.True(manifestFirst);
        Assert.Equal(audio.Sum(f => f.Bytes), result.GetProperty("freedBytes").GetInt64());
        var after = FilesOf(recordingId);
        Assert.All(audio, f => Assert.False(after.ContainsKey(f.File), f.File));
        foreach (var (file, bytes) in before.Where(kv => audio.All(a => a.File != kv.Key) && kv.Key != ProjectLayout.ManifestFile && kv.Key != "history.jsonl"))
        {
            Assert.Equal(bytes, after[file]);
        }

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal((ProjectAudioRemoved.ByUser, audio.Sum(f => f.Bytes)), (manifest.AudioRemoved!.Reason, manifest.AudioRemoved.BytesFreed));
        Assert.Equal(["mic", "system"], manifest.AudioRemoved.TrackIds.Order(StringComparer.Ordinal));
        Assert.Null(manifest.Mix);
        Assert.Null(manifest.Peaks);
        Assert.All(manifest.Tracks, t => Assert.Null(t.Sha256));
        Assert.Equal(2, manifest.Tracks.Count);
        Assert.DoesNotContain(manifest.Integrity.Files.Keys, k => k.StartsWith("tracks/", StringComparison.Ordinal) || k.StartsWith("mix", StringComparison.Ordinal));

        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Summary.StartsWith("Deleted the audio · ", StringComparison.Ordinal) && h.Detail?.Contains("2 audio tracks and the mix removed. The transcript and documents are kept", StringComparison.Ordinal) == true);

        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));
        Assert.Equal(JsonValueKind.Null, project.GetProperty("mixUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, project.GetProperty("peaksUrl").ValueKind);
        Assert.Equal(audio.Sum(f => f.Bytes), project.GetProperty("audioRemoved").GetProperty("bytesFreed").GetInt64());
        Assert.Equal(2, project.GetProperty("tracks").GetArrayLength());

        // Still listed, with what is left.
        var row = Assert.Single((await _host.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
        Assert.Equal(JsonValueKind.String, row.GetProperty("audioRemoved").GetProperty("at").ValueKind);

        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("audio.remove", JsonSerializer.Serialize(new { recordingId }))));

        // Every stage reads the audio, so nothing can run again.
        var refused = await Assert.ThrowsAsync<Memento.Core.Bridge.BridgeException>(() => _host.Processing.RetryAsync(recordingId, StageNames.Optimize, null, CancellationToken.None));
        Assert.Equal("bridge.invalidParams", refused.Code);
    }

    [Fact]
    public async Task ExportSaysTheAudioWasDeleted()
    {
        var recordingId = await RecordAsync("Exported");
        await _host.ResultAsync("audio.remove", JsonSerializer.Serialize(new { recordingId }));
        var estimate = await _host.ResultAsync(
            "export.estimate",
            JsonSerializer.Serialize(new { recordingId, selection = new { audioMixed = new { on = true, format = "flac" }, tracks = new { on = true, format = "flac" } } }));
        var unavailable = estimate.GetProperty("unavailable").EnumerateArray().Select(u => u.GetProperty("reason").GetString()).ToList();
        Assert.Equal(["Audio was deleted", "Audio was deleted"], unavailable);
    }

    [Fact]
    public async Task RefusalsChangeNothing()
    {
        Assert.Equal("project.notFound", Error(await _host.CallAsync("audio.remove", """{"recordingId":"20260101-000000-zzzzzz"}""")));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("audio.remove", "{}")));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("audio.removeEstimate", """{"recordingIds":null,"olderThanDays":0}""")));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("audio.removeEstimate", """{"recordingIds":null,"olderThanDays":1,"x":1}""")));

        var (sessionId, recordingId) = await _host.StartAsync("Still recording", Mic);
        Assert.Equal("project.recording", Error(await _host.CallAsync("audio.remove", JsonSerializer.Serialize(new { recordingId }))));
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _host.Recordings.WhenIdleAsync();
        await _host.Processing.WhenIdleAsync();
        Assert.Null((await _host.Store.LoadAsync(recordingId, CancellationToken.None)).AudioRemoved);

        // A stage waiting for this recording reads its audio.
        await _host.Get<ProjectCatalog>().UpdateAsync(recordingId, m => m with { Stages = [new Memento.Core.Bridge.Contracts.StageStatus("transcript", "queued", null, "Queued")] }, CancellationToken.None);
        Assert.Equal("library.busy", Error(await _host.CallAsync("audio.remove", JsonSerializer.Serialize(new { recordingId }))));
        Assert.Null((await _host.Store.LoadAsync(recordingId, CancellationToken.None)).AudioRemoved);
    }

    [Fact]
    public async Task TheSettingTakesExactlyTheDesignedAges()
    {
        var storage = (await _host.ResultAsync("settings.get")).GetProperty("storage");
        Assert.Equal(JsonValueKind.Null, storage.GetProperty("deleteAudioOlderThanDays").ValueKind);
        foreach (var days in new[] { 1, 7, 30, 90, 365, 730, 1095, 1460, 1825 })
        {
            var set = await _host.ResultAsync("settings.set", JsonSerializer.Serialize(new { storage = new { deleteAudioOlderThanDays = days } }));
            Assert.Equal(days, set.GetProperty("storage").GetProperty("deleteAudioOlderThanDays").GetInt32());
        }

        foreach (var bad in new[] { """{"storage":{"deleteAudioOlderThanDays":0}}""", """{"storage":{"deleteAudioOlderThanDays":14}}""", """{"storage":{"deleteAudioOlderThanDays":"week"}}""" })
        {
            Assert.Equal("settings.invalidValue", Error(await _host.CallAsync("settings.set", bad)));
        }

        Assert.Equal(1825, _host.Settings.Current.Storage.DeleteAudioOlderThanDays);
        var cleared = await _host.ResultAsync("settings.set", """{"storage":{"deleteAudioOlderThanDays":null}}""");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("storage").GetProperty("deleteAudioOlderThanDays").ValueKind);
        Assert.Equal(["a day", "a week", "a month", "90 days", "a year", "2 years", "3 years", "4 years", "5 years"], AudioRemovalService.AgeChoices.Select(AudioRemovalService.AgeName));
    }

    [Fact]
    public async Task AudioOlderThanTheSettingIsEstimatedAndSweptButNeverWhileBusy()
    {
        var old = await RecordAsync("Old call");
        var recent = await RecordAsync("Recent call");
        var oldBytes = AudioRemovalService.AudioFiles(await _host.Store.LoadAsync(old, CancellationToken.None), _host.Store.GetProjectFolder(old)).Sum(f => f.Bytes);
        Assert.Equal("""{"recordings":0,"bytes":0}""", (await _host.ResultAsync("audio.removeEstimate", """{"recordingIds":null,"olderThanDays":30}""")).GetRawText());

        // Nothing happens at Never.
        Assert.Equal(0, await _host.Get<AudioRemovalService>().SweepAsync(CancellationToken.None));

        await _host.Get<ProjectCatalog>().UpdateAsync(old, m => m with { CreatedAt = m.CreatedAt.AddDays(-40) }, CancellationToken.None);
        Assert.Equal($$"""{"recordings":1,"bytes":{{oldBytes}}}""", (await _host.ResultAsync("audio.removeEstimate", """{"recordingIds":null,"olderThanDays":30}""")).GetRawText());
        Assert.Equal($$"""{"recordings":1,"bytes":{{oldBytes}}}""", (await _host.ResultAsync("audio.removeEstimate", JsonSerializer.Serialize(new { recordingIds = new[] { old }, olderThanDays = (int?)null }))).GetRawText());
        await _host.ResultAsync("settings.set", """{"storage":{"deleteAudioOlderThanDays":30}}""");

        // Never while a recording runs.
        var (sessionId, _) = await _host.StartAsync("Busy", Mic);
        Assert.Equal(0, await _host.Get<AudioRemovalService>().SweepAsync(CancellationToken.None));
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _host.Recordings.WhenIdleAsync();
        await _host.Processing.WhenIdleAsync();

        Assert.Equal(1, await _host.Get<AudioRemovalService>().SweepAsync(CancellationToken.None));
        Assert.Equal(ProjectAudioRemoved.ByAge, (await _host.Store.LoadAsync(old, CancellationToken.None)).AudioRemoved!.Reason);
        Assert.Null((await _host.Store.LoadAsync(recent, CancellationToken.None)).AudioRemoved);
        var history = await _host.Store.ReadHistoryAsync(old, CancellationToken.None);
        Assert.Contains(history, h => h.Detail?.StartsWith("Settings › Storage deletes audio tracks older than a month.", StringComparison.Ordinal) == true);

        // Swept once: the next sweep finds nothing.
        Assert.Equal(0, await _host.Get<AudioRemovalService>().SweepAsync(CancellationToken.None));
    }
}
