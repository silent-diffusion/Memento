using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Tests.Fakes;
using Memento.Core.Video;
using Microsoft.Extensions.DependencyInjection;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Recording;

/// <summary>
/// "Resume recording" (2.1, ARCHITECTURE.md §5.11): a stopped recording takes a new segment through
/// <c>recording.resumeStopped</c>. The earlier tracks stay finalized and untouched, the new tracks are placed after them on
/// the timeline with the real gap as a pause, the mix covers both, and recovery repairs only the segment that crashed.
/// The simulated engine runs in manual time.
/// </summary>
public sealed class ResumeRecordingTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeVideoCapture _capture = new();
    private readonly FakeVideoFiles _files = new();
    private readonly BridgeTestHost _host;

    public ResumeRecordingTests()
    {
        _host = NewHost();
    }

    public void Dispose()
    {
        _host.Dispose();
        _directory.Dispose();
    }

    private BridgeTestHost NewHost() => new(directory: _directory, configure: services =>
    {
        services.AddSingleton<IVideoCapture>(_capture);
        services.AddSingleton<IVideoFiles>(_files);
    });

    private Task<JsonElement> ResumeAsync(string recordingId, string[]? sourceIds = null) =>
        _host.ResultAsync("recording.resumeStopped", JsonSerializer.Serialize(sourceIds is null ? (object)new { recordingId } : new { recordingId, sourceIds }));

    private async Task StopAsync(string sessionId)
    {
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _host.Recordings.WhenIdleAsync();
    }

    private static string? ErrorCode(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    [Fact]
    public async Task AResumedRecordingAppendsASegmentAfterTheFirstWithTheGapAsAPauseAndOneMixOverBoth()
    {
        var recordingId = await _host.RecordAsync("Design review", 3, Mic, SystemAudio);
        var folder = _host.Store.GetProjectFolder(recordingId);
        var first = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        var micHash = first.Tracks.Single(t => t.Id == "mic").Sha256;
        var micWritten = File.GetLastWriteTimeUtc(Path.Combine(folder, "tracks", "mic.wav"));
        Assert.Single(first.Segments);
        Assert.True((await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }))).GetProperty("canResume").GetBoolean());

        await Task.Delay(50); // a measurable gap
        var resumed = await ResumeAsync(recordingId);
        Assert.Equal(recordingId, resumed.GetProperty("recordingId").GetString());
        var sessionId = resumed.GetProperty("sessionId").GetString()!;

        // While the segment records: the state file is on the recording timeline, the payload says which segment.
        var current = (await _host.ResultAsync("recording.current")).GetProperty("session");
        Assert.Equal(2, current.GetProperty("segment").GetInt32());
        Assert.Equal(3000, current.GetProperty("segmentOffsetMs").GetInt64());
        var state = await _host.Store.ReadRecordingStateAsync(recordingId, CancellationToken.None);
        Assert.Equal(2, state!.Segment);
        Assert.Equal(3000, state.SegmentOffsetMs);
        Assert.All(state.Tracks, t => Assert.Equal(3000, t.StartOffsetMs));
        Assert.Equal(["mic-2", "system-2"], state.Tracks.Select(t => t.TrackId).Order(StringComparer.Ordinal));
        Assert.Equal(ProjectStates.Recording, (await _host.Store.LoadAsync(recordingId, CancellationToken.None)).State);

        _host.Session.Advance(TimeSpan.FromSeconds(1));
        var highlight = (await _host.ResultAsync("recording.markHighlight", JsonSerializer.Serialize(new { sessionId, note = "after the break" }))).GetProperty("highlight");
        Assert.Equal(4000, highlight.GetProperty("atMs").GetInt64());
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await StopAsync(sessionId);

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.Equal(5000, manifest.DurationMs);

        // Segments: end to end, the second after a real gap.
        Assert.Equal(2, manifest.Segments.Count);
        Assert.Equal((1, 0L, 3000L), (manifest.Segments[0].Index, manifest.Segments[0].OffsetMs, manifest.Segments[0].DurationMs));
        Assert.Equal((2, 3000L, 2000L), (manifest.Segments[1].Index, manifest.Segments[1].OffsetMs, manifest.Segments[1].DurationMs));
        Assert.True(manifest.Segments[1].GapMs >= 40, $"gap {manifest.Segments[1].GapMs} ms");
        Assert.NotNull(manifest.Segments[1].EndedAt);
        var gap = Assert.Single(manifest.Pauses);
        Assert.Equal(3000, gap.AtMs);
        Assert.Equal(manifest.Segments[1].GapMs, gap.DurationMs);

        // Tracks: the first segment's untouched, the new ones after them, every one hashed.
        Assert.Equal(["mic", "system", "mic-2", "system-2"], manifest.Tracks.Select(t => t.Id));
        var mic = manifest.Tracks.Single(t => t.Id == "mic");
        Assert.Equal(micHash, mic.Sha256);
        Assert.Null(mic.Segment);
        Assert.Equal(micWritten, File.GetLastWriteTimeUtc(Path.Combine(folder, "tracks", "mic.wav")));
        Assert.True(File.GetAttributes(Path.Combine(folder, "tracks", "mic.wav")).HasFlag(FileAttributes.ReadOnly));
        var mic2 = manifest.Tracks.Single(t => t.Id == "mic-2");
        Assert.Equal((2, 3000L, 2000L, "tracks/mic-2.wav"), (mic2.Segment ?? 0, mic2.StartOffsetMs, mic2.DurationMs, mic2.File));
        Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, "tracks", "mic-2.wav"), CancellationToken.None), mic2.Sha256);
        Assert.Equal(["mix.wav", "tracks/mic-2.wav", "tracks/mic.wav", "tracks/system-2.wav", "tracks/system.wav"], manifest.Integrity.Files.Keys.Order(StringComparer.Ordinal));

        // One mix over the whole timeline (the gap is a pause, not silence), and its peaks.
        Assert.Equal(5000, manifest.Mix!.DurationMs);
        Assert.Equal(5000, WavInfo.Read(Path.Combine(folder, "mix.wav")).DurationMs);
        Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, "mix.wav"), CancellationToken.None), manifest.Mix.Sha256);
        using (var peaks = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "peaks.json"))))
        {
            Assert.Equal(100, peaks.RootElement.GetProperty("peaks").GetArrayLength());
        }

        Assert.False(_host.Store.HasRecordingState(recordingId));
        Assert.Empty(Directory.EnumerateFiles(folder, "mix.segments*"));

        // History says what happened; Review and the Library show where it was resumed.
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Summary.StartsWith("Recording resumed at ", StringComparison.Ordinal) && h.Detail!.Contains("Segment 2", StringComparison.Ordinal));
        Assert.Contains(history, h => h.Summary == "Recorded 0:02 more · 0:05 in all");
        Assert.Contains(history, h => h.Summary.StartsWith("Resumed recording saved · 2 segments", StringComparison.Ordinal));
        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));
        Assert.Equal(2, project.GetProperty("segments").GetArrayLength());
        Assert.Equal(3000, project.GetProperty("segments")[1].GetProperty("offsetMs").GetInt64());
        Assert.Equal(manifest.Segments[1].StartedAt, project.GetProperty("summary").GetProperty("resumedAt")[0].GetDateTimeOffset());
        Assert.Contains("?v=", project.GetProperty("mixUrl").GetString(), StringComparison.Ordinal);
        var row = Assert.Single((await _host.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
        Assert.Equal(manifest.Segments[1].StartedAt, row.GetProperty("resumedAt")[0].GetDateTimeOffset());
        Assert.Equal(5000, row.GetProperty("durationMs").GetInt64());
        var annotations = await _host.Store.LoadAnnotationsAsync(recordingId, CancellationToken.None);
        Assert.Equal(4000, Assert.Single(annotations.Highlights).AtMs);
    }

    [Fact]
    public async Task ResumingTwiceAddsAThirdSegmentAndExplicitSourcesAreHonoured()
    {
        var recordingId = await _host.RecordAsync("Workshop", 2, Mic);
        var second = await ResumeAsync(recordingId, [SystemAudio]);
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await StopAsync(second.GetProperty("sessionId").GetString()!);
        var third = await ResumeAsync(recordingId);
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await StopAsync(third.GetProperty("sessionId").GetString()!);

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal([1, 2, 3], manifest.Segments.Select(s => s.Index));
        Assert.Equal([0L, 2000L, 3000L], manifest.Segments.Select(s => s.OffsetMs));
        Assert.Equal(["mic", "system", "system-2"], manifest.Tracks.Select(t => t.Id));
        Assert.Equal([0L, 2000L, 3000L], manifest.Tracks.Select(t => t.StartOffsetMs));
        Assert.Equal(4000, manifest.DurationMs);
        Assert.Equal(4000, manifest.Mix!.DurationMs);
        Assert.Equal(2, manifest.Pauses.Count);
    }

    [Fact]
    public async Task TheStagesThatRanAreQueuedAgainOverTheWholeRecording()
    {
        var recordingId = await _host.RecordAsync("Interview", 2, Mic);
        await _host.Get<ProjectCatalog>().UpdateAsync(
            recordingId,
            m => m with { Stages = [.. m.Stages, new StageStatus(StageNames.Transcript, StageStates.Done, null, "Done")] },
            CancellationToken.None);

        var resumed = await ResumeAsync(recordingId);
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await StopAsync(resumed.GetProperty("sessionId").GetString()!);

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.DoesNotContain(manifest.Stages, s => s.Stage == StageNames.Transcript && s.State == StageStates.Done);
        Assert.True(manifest.Processing!.Retranscribe);
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Summary.StartsWith("Resumed recording saved", StringComparison.Ordinal) && h.Detail!.Contains("earlier tracks were kept", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARecordingFromAnotherDayAnImportOrOneWhileRecordingIsNotResumed()
    {
        var old = await _host.RecordAsync("Yesterday", 1, Mic);
        await _host.Get<ProjectCatalog>().UpdateAsync(old, m => m with { CreatedAt = m.CreatedAt.AddDays(-2) }, CancellationToken.None);
        var response = await _host.CallAsync("recording.resumeStopped", JsonSerializer.Serialize(new { recordingId = old }));
        Assert.Equal(DomainErrorCodes.RecordingCannotResume, ErrorCode(response));
        Assert.Contains("only recordings started today can be resumed", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId = old }));
        Assert.False(project.GetProperty("canResume").GetBoolean());
        Assert.Contains("only recordings started today", project.GetProperty("resumeNote").GetString(), StringComparison.Ordinal);

        var today = await _host.RecordAsync("Today", 1, Mic);
        var (sessionId, _) = await _host.StartAsync("Running", Mic);
        Assert.Equal(DomainErrorCodes.RecordingAlreadyActive, ErrorCode(await _host.CallAsync("recording.resumeStopped", JsonSerializer.Serialize(new { recordingId = today }))));
        Assert.False((await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId = today }))).GetProperty("canResume").GetBoolean());
        await StopAsync(sessionId);

        Assert.Equal(DomainErrorCodes.ProjectNotFound, ErrorCode(await _host.CallAsync("recording.resumeStopped", """{"recordingId":"20261006-100000-zzzzzz"}""")));
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("recording.resumeStopped", """{"recordingId":""}""")));
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("recording.resumeStopped", """{"recordingId":"x","extra":1}""")));

        // Nothing was changed by the refusals.
        var unchanged = await _host.Store.LoadAsync(old, CancellationToken.None);
        Assert.Single(unchanged.Segments);
        Assert.Equal(ProjectStates.Ready, unchanged.State);
    }

    [Fact]
    public void ThePolicyNamesEveryReason()
    {
        var now = new DateTimeOffset(2026, 10, 11, 15, 0, 0, TimeSpan.FromHours(2));
        var ready = new ProjectManifest
        {
            Id = "r",
            CreatedAt = now.AddHours(-3),
            State = ProjectStates.Ready,
            Details = new ProjectDetails { Title = "Sync" },
            Tracks = [new ProjectTrack { Id = "mic", SourceId = "mic:x", SourceKind = "microphone", Name = "Mic", File = "tracks/mic.flac", Sha256 = "ab" }],
        };

        Assert.Null(ResumePolicy.WhyNot(ready, now));
        Assert.Null(ResumePolicy.WhyNot(ready with { State = ProjectStates.Recovered }, now));
        Assert.Contains("imported", ResumePolicy.WhyNot(ready with { ImportedFrom = new ProjectImportSource { Path = @"C:\media\a.mp3", Name = "a.mp3" } }, now), StringComparison.Ordinal);
        Assert.Contains("still being recorded or saved", ResumePolicy.WhyNot(ready with { State = ProjectStates.Finalizing }, now), StringComparison.Ordinal);
        Assert.Contains("not saved completely", ResumePolicy.WhyNot(ready with { State = ProjectStates.Failed }, now), StringComparison.Ordinal);
        Assert.Contains("only recordings started today", ResumePolicy.WhyNot(ready with { CreatedAt = now.AddDays(-1) }, now), StringComparison.Ordinal);
        Assert.Contains("Keep only the mix", ResumePolicy.WhyNot(ready with { MixOnly = new ProjectMixOnly(now, ["mic"], 10) }, now), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACrashDuringAResumedSegmentRepairsOnlyThatSegmentAndKeepsTheEarlierOne()
    {
        // A first "process" of its own, killed mid-segment (the class's host shares the folder and is never started here).
        using var directory = new TempDirectory();
        var first = new BridgeTestHost(directory: directory);
        await first.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":5}}""");
        var recordingId = await first.RecordAsync("Long session", 4, Mic, SystemAudio);
        var folder = first.Store.GetProjectFolder(recordingId);
        var before = await first.Store.LoadAsync(recordingId, CancellationToken.None);

        await first.ResultAsync("recording.resumeStopped", JsonSerializer.Serialize(new { recordingId }));
        first.Session.Advance(TimeSpan.FromSeconds(6));
        await first.Session.DrainAsync();
        await first.WaitForCheckpointWrittenAsync();
        first.Session.Advance(TimeSpan.FromSeconds(2));
        await first.Session.SimulateCrashAsync(flushBufferedSamples: false);
        first.DisposeServicesOnly();

        using var second = new BridgeTestHost(directory: directory);
        Assert.Equal([recordingId], await second.Recovery.RunAsync(CancellationToken.None));
        var manifest = await second.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(ProjectStates.Recovered, manifest.State);

        // The first segment is exactly as it was.
        foreach (var track in before.Tracks)
        {
            var kept = manifest.Tracks.Single(t => t.Id == track.Id);
            Assert.Equal((track.Sha256, track.File, track.StartOffsetMs), (kept.Sha256, kept.File, kept.StartOffsetMs));
        }

        // The segment's tracks were repaired and finalized on the recording timeline.
        var segmentTracks = manifest.Tracks.Where(t => t.Segment == 2).ToList();
        Assert.Equal(["mic-2", "system-2"], segmentTracks.Select(t => t.Id));
        Assert.All(segmentTracks, t => Assert.Equal(4000, t.StartOffsetMs));
        Assert.All(segmentTracks, t => Assert.NotNull(t.Sha256));
        Assert.Equal(2, manifest.Recovery!.TracksTotal);
        Assert.InRange(manifest.DurationMs, 4000 + 5000, 4000 + 8000);
        Assert.Equal(manifest.DurationMs, manifest.Mix!.DurationMs);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.Equal(manifest.DurationMs - 4000, manifest.Segments[1].DurationMs);
        Assert.Single(manifest.Pauses); // the gap
        Assert.False(second.Store.HasRecordingState(recordingId));
        Assert.True(File.Exists(Path.Combine(folder, "tracks", "mic.wav")));
    }

    [Fact]
    public async Task VideoIsResumedWithItsOwnTrackAfterTheEarlierSegment()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Screen walk-through", Mic, FakeVideoCapture.Display1);
        _host.Session.Advance(TimeSpan.FromSeconds(3));
        await StopAsync(sessionId);

        var resumed = await ResumeAsync(recordingId);
        Assert.Equal(2, _capture.Started.Count);
        Assert.Equal("screen-2", _capture.Started[1].TrackId);
        var state = await _host.Store.ReadRecordingStateAsync(recordingId, CancellationToken.None);
        Assert.All(Assert.Single(state!.VideoTracks).Parts, p => Assert.True(p.StartOffsetMs >= 3000));
        _host.Session.Advance(TimeSpan.FromSeconds(2));
        await StopAsync(resumed.GetProperty("sessionId").GetString()!);

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.True(manifest.HasVideo);
        Assert.Equal(["screen", "screen-2"], manifest.VideoTracks.Select(v => v.Id));
        Assert.Null(manifest.VideoTracks[0].Segment);
        var later = manifest.VideoTracks[1];
        Assert.Equal(2, later.Segment);
        Assert.True(later.StartOffsetMs >= 3000, $"starts at {later.StartOffsetMs} ms");
        Assert.All(later.Parts, p => Assert.True(p.StartOffsetMs >= 3000));
        Assert.True(manifest.VideoTracks[0].StartOffsetMs < 3000);
    }

    [Fact]
    public void AV5ManifestGetsOneSegment()
    {
        var v4 = new System.Text.Json.Nodes.JsonObject
        {
            ["schemaVersion"] = 5,
            ["id"] = "20261006-100000-k3f9ab",
            ["createdAt"] = "2026-10-06T10:00:00+01:00",
            ["durationMs"] = 61_000,
        };
        var migrated = ProjectManifestMigrator.Default.Migrate(v4);
        Assert.True(migrated.Migrated);
        Assert.Equal(ProjectManifest.CurrentSchemaVersion, migrated.Manifest["schemaVersion"]!.GetValue<int>());
        var manifest = migrated.Manifest.Deserialize(ProjectJsonContext.Default.ProjectManifest)!;
        var segment = Assert.Single(manifest.Segments);
        Assert.Equal((1, 0L, 61_000L, 0L), (segment.Index, segment.OffsetMs, segment.DurationMs, segment.GapMs));
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1)), segment.StartedAt);

        var noDate = ProjectManifestMigrator.Default.Migrate(new System.Text.Json.Nodes.JsonObject { ["schemaVersion"] = 5, ["segments"] = "bad" });
        Assert.Empty(noDate.Manifest["segments"]!.AsArray());
    }
}
