using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Memento.Core.Video;
using Microsoft.Extensions.DependencyInjection;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Video;

/// <summary>
/// The video tracks of a recording through the bridge (ARCHITECTURE.md §5.x, DESIGN.md §18.6): started, paused,
/// checkpointed and stopped with the audio session, switched or ended by <c>recording.setVideoSource</c>, failing alone.
/// The simulated audio engine runs in manual time; video capture is <see cref="FakeVideoCapture"/>.
/// </summary>
public sealed class VideoRecordingTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeVideoCapture _capture = new();
    private readonly FakeVideoFiles _files = new();
    private readonly BridgeTestHost _host;

    public VideoRecordingTests()
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

    private Task<JsonElement> Session(string method, string sessionId, object? extra = null)
    {
        var parameters = new Dictionary<string, object?> { ["sessionId"] = sessionId };
        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                parameters[property.Name] = property.Value;
            }
        }

        return _host.ResultAsync(method, JsonSerializer.Serialize(parameters));
    }

    private Task<JsonElement> SetVideoSourceAsync(string sessionId, string role, string? sourceId) =>
        _host.CallAsync("recording.setVideoSource", JsonSerializer.Serialize(new { sessionId, role, sourceId }));

    private async Task StopAsync(string sessionId)
    {
        await Session("recording.stop", sessionId);
        await _host.Recordings.WhenIdleAsync();
    }

    [Fact]
    public async Task AScreenTrackRecordsOnTheAudioTimelineAndIsCheckpointedPausedStoppedCheckedAndPlayable()
    {
        await _host.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":5}}""");
        var (sessionId, recordingId) = await _host.StartAsync("Screen share", Mic, FakeVideoCapture.Display1);

        var track = Assert.Single(_capture.Started);
        Assert.Equal(("screen", "screen", FakeVideoCapture.Display1), (track.TrackId, track.Request.Role, track.Request.SourceId));
        Assert.Equal(_host.Session.TimelineOriginQpc, track.Request.TimelineOriginQpc);
        Assert.Equal(_host.Store.GetProjectFolder(recordingId), track.Request.ProjectFolder);
        var current = (await _host.ResultAsync("recording.current")).GetProperty("session");
        Assert.Equal("screen", current.GetProperty("videoTracks")[0].GetProperty("trackId").GetString());
        Assert.True((await _host.Store.LoadAsync(recordingId, CancellationToken.None)).HasVideo);

        // A checkpoint flushes the video file and names it in recording.state.json.
        _host.Session.Advance(TimeSpan.FromSeconds(6));
        await _host.Session.DrainAsync();
        await _host.WaitForCheckpointWrittenAsync();
        var state = await _host.Store.ReadRecordingStateAsync(recordingId, CancellationToken.None);
        Assert.Equal("tracks/screen.mp4", Assert.Single(Assert.Single(state!.VideoTracks).Parts).File);
        Assert.True(track.Flushes >= 1);

        // Pause and resume reach the video track with the audio.
        await Session("recording.pause", sessionId);
        await Session("recording.resume", sessionId);
        Assert.Equal((1, 1), (track.Pauses, track.Resumes));

        // The Encoder card's figures arrive once a second.
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        var status = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingVideoStatus);
        Assert.Equal(sessionId, status.GetProperty("sessionId").GetString());
        Assert.True(status.GetProperty("videoTracks")[0].GetProperty("hardware").GetBoolean());

        await StopAsync(sessionId);
        await _host.WaitForStageLabelAsync(recordingId, StageNames.Video, "Done");

        Assert.True(track.Stopped);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        var stored = Assert.Single(manifest.VideoTracks);
        Assert.Equal(("screen", 40L, 10_000L, 300L), (stored.Id, stored.StartOffsetMs, stored.DurationMs, stored.FramesWritten));
        Assert.NotNull(stored.Sha256);
        Assert.Equal(stored.Sha256, manifest.Integrity.Files["tracks/screen.mp4"]);
        Assert.True(manifest.HasVideo);
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "recorded" && h.Event == "started" && h.Detail!.Contains("video: screen (Display 1)", StringComparison.Ordinal));
        Assert.Contains(history, h => h.Stage == "recorded" && h.Event == "completed" && h.Detail!.Contains("1 video track (screen 0:10)", StringComparison.Ordinal));
        Assert.Contains(history, h => h.Stage == "video" && h.Event == "completed" && h.Summary == "Video checked" && h.Detail!.Contains("screen: Display 1 · 1920 × 1080 · 30 fps", StringComparison.Ordinal));

        // Review gets the file and where it sits; the Library row says it has video.
        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));
        var video = project.GetProperty("video");
        var info = video.GetProperty("tracks")[0];
        Assert.Equal(("screen", "display", "Display 1"), (info.GetProperty("id").GetString(), info.GetProperty("sourceKind").GetString(), info.GetProperty("sourceName").GetString()));
        Assert.Equal($"https://library.memento/{recordingId}/tracks/screen.mp4", info.GetProperty("parts")[0].GetProperty("url").GetString());
        Assert.Equal(40, info.GetProperty("startOffsetMs").GetInt64());
        Assert.Equal(40, info.GetProperty("parts")[0].GetProperty("startOffsetMs").GetInt64());
        Assert.Equal(JsonValueKind.Null, info.GetProperty("endReason").ValueKind);
        Assert.Equal(4096, video.GetProperty("sizeBytes").GetInt64());
        Assert.False(video.GetProperty("pip").GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, video.GetProperty("removed").ValueKind);
        var row = Assert.Single((await _host.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
        Assert.Equal("""{"roles":["screen"],"height":1080,"removedAt":null}""", row.GetProperty("video").GetRawText());

        // A finished video stage is housekeeping: rows leave it out, as they do a finished stored stage.
        Assert.DoesNotContain(row.GetProperty("stages").EnumerateArray(), s => s.GetProperty("stage").GetString() == "video");
    }

    [Fact]
    public async Task AVideoSourceThatCannotStartIsReportedAndTheAudioRecordsAnyway()
    {
        _capture.Refuse[FakeVideoCapture.Camera] = FakeVideoCapture.CameraInUse();

        var (sessionId, recordingId) = await _host.StartAsync("Interview", Mic, FakeVideoCapture.Camera);

        var problem = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingVideoProblem);
        Assert.Equal((sessionId, "camera", "waiting", "video.cameraInUse"), (problem.GetProperty("sessionId").GetString(), problem.GetProperty("trackId").GetString(), problem.GetProperty("state").GetString(), problem.GetProperty("code").GetString()));
        Assert.Equal((FakeVideoCapture.Camera, "Test camera", "Zoom"), (problem.GetProperty("sourceId").GetString(), problem.GetProperty("sourceName").GetString(), problem.GetProperty("detail").GetString()));
        Assert.StartsWith("Test camera is in use", problem.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("recording", (await _host.ResultAsync("recording.current")).GetProperty("session").GetProperty("state").GetString());

        _host.Session.Advance(TimeSpan.FromSeconds(2));
        await StopAsync(sessionId);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Empty(manifest.VideoTracks);
        Assert.False(manifest.HasVideo);
        Assert.Null(StageListOf(manifest, StageNames.Video));
    }

    [Fact]
    public async Task VideoAloneIsNotARecording()
    {
        var response = await _host.CallAsync("recording.start", JsonSerializer.Serialize(new { title = "Screen only", type = "meeting", sourceIds = new[] { FakeVideoCapture.Display1 } }));

        Assert.Equal("recording.noSources", response.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("never on its own", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_capture.Started);
    }

    [Fact]
    public async Task TheScreenTrackSwitchesSourceEndsAndStartsAgainAsANewTrack()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Demo", Mic, FakeVideoCapture.Display1);
        var first = Assert.Single(_capture.Started);

        // Another display or a window continues the same track.
        var switched = (await SetVideoSourceAsync(sessionId, "screen", FakeVideoCapture.Window)).GetProperty("result").GetProperty("videoTracks");
        Assert.Equal([FakeVideoCapture.Window], first.Switches);
        Assert.Equal(FakeVideoCapture.Window, Assert.Single(switched.EnumerateArray()).GetProperty("sourceId").GetString());

        // A camera id for the screen role is refused; the track is untouched.
        var wrong = await SetVideoSourceAsync(sessionId, "screen", FakeVideoCapture.Camera);
        Assert.Equal("bridge.invalidParams", wrong.GetProperty("error").GetProperty("code").GetString());

        // A failed switch keeps the track and says why.
        first.FailNextSwitch = new VideoStartException(DomainErrorCodes.VideoProtectedContent, "“Player” shows protected content.");
        var refused = await SetVideoSourceAsync(sessionId, "screen", FakeVideoCapture.Display2);
        Assert.Equal("video.protectedContent", refused.GetProperty("error").GetProperty("code").GetString());

        // null ends it; choosing a source again starts screen-2.
        _host.Session.Advance(TimeSpan.FromSeconds(3));
        var ended = (await SetVideoSourceAsync(sessionId, "screen", null)).GetProperty("result").GetProperty("videoTracks");
        Assert.True(first.Stopped);
        Assert.Empty(ended.EnumerateArray());
        await SetVideoSourceAsync(sessionId, "screen", FakeVideoCapture.Display1);
        Assert.Equal(["screen", "screen-2"], _capture.Started.Select(t => t.TrackId));

        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await StopAsync(sessionId);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(["screen", "screen-2"], manifest.VideoTracks.Select(t => t.Id).Order(StringComparer.Ordinal));
        var off = manifest.VideoTracks.Single(t => t.Id == "screen");
        Assert.Equal(("disabled", 3000L), (off.EndReason, off.EndedEarlyAtMs!.Value));
    }

    [Fact]
    public async Task ATrackStartedWhileRecordingLeavesThePausedTimeOutOfItsTimeline()
    {
        var (sessionId, _) = await _host.StartAsync("Late camera", Mic);
        await Session("recording.pause", sessionId);
        _host.Session.Advance(TimeSpan.FromSeconds(2));
        await Session("recording.resume", sessionId);

        await SetVideoSourceAsync(sessionId, "camera", FakeVideoCapture.Camera);

        var camera = Assert.Single(_capture.Started);
        var paused = _host.Session.Pauses.Sum(p => p.DurationMs ?? 0);
        Assert.True(paused > 0);
        Assert.Equal(_host.Session.TimelineOriginQpc + (paused * VideoClock.TicksPerMillisecond), camera.Request.TimelineOriginQpc);
        Assert.Equal("camera", camera.TrackId);
        await StopAsync(sessionId);
    }

    [Fact]
    public async Task ProblemsAreToldWithTheirSentenceAndAnEndedTrackIsReplacedByANewOne()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Window", Mic, FakeVideoCapture.Window);
        var track = Assert.Single(_capture.Started);

        track.Raise("ended", "video.windowClosed", "“Planning notes” was closed at 0:00:05. The video track ends there and everything before it is saved.", 5000);

        var problem = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingVideoProblem, p => p.GetProperty("code").GetString() == "video.windowClosed");
        Assert.Equal(("screen", "ended", FakeVideoCapture.Window, "Planning notes", 5000L), (problem.GetProperty("trackId").GetString(), problem.GetProperty("state").GetString(), problem.GetProperty("sourceId").GetString(), problem.GetProperty("sourceName").GetString(), problem.GetProperty("atMs").GetInt64()));

        // The ended track stays listed until it is replaced.
        var status = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingVideoStatus, p => p.GetProperty("videoTracks").EnumerateArray().Any(t => t.GetProperty("state").GetString() == "ended"));
        Assert.Equal("screen", status.GetProperty("videoTracks")[0].GetProperty("trackId").GetString());

        // "Capture Display 1 instead": a new track from here.
        await SetVideoSourceAsync(sessionId, "screen", FakeVideoCapture.Display1);
        Assert.Equal(2, _capture.Started.Count);
        Assert.Empty(track.Switches);
        await StopAsync(sessionId);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal("sourceLost", manifest.VideoTracks.Single(t => t.Id == "screen").EndReason);
    }

    [Fact]
    public async Task LowDiskStopsTheVideoFirstAndTheAudioGoesOn()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Long talk", Mic, FakeVideoCapture.Display1);
        var track = Assert.Single(_capture.Started);

        _host.FreeSpace.FreeBytes = 600L * 1024 * 1024;

        var problem = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingVideoProblem, p => p.GetProperty("code").GetString() == "video.diskFull");
        Assert.Equal("stopped", problem.GetProperty("state").GetString());
        Assert.StartsWith("Low disk space · 600 MB free. The screen video stopped at", problem.GetProperty("message").GetString(), StringComparison.Ordinal);
        await WaitUntilAsync(() => track.Stopped, "the video track to stop");
        Assert.Equal("recording", (await _host.ResultAsync("recording.current")).GetProperty("session").GetProperty("state").GetString());

        _host.FreeSpace.FreeBytes = null;
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await StopAsync(sessionId);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal("diskFull", Assert.Single(manifest.VideoTracks).EndReason);
        Assert.Equal(ProjectStates.Ready, manifest.State);
    }

    [Fact]
    public async Task TheInsetIsRecordedWithItsTimesForTheExport()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Talk", Mic, FakeVideoCapture.Display1, FakeVideoCapture.Camera);
        Assert.Equal(["screen", "camera"], _capture.Started.Select(t => t.TrackId));

        _host.Session.Advance(TimeSpan.FromSeconds(2));
        await Session("recording.setPip", sessionId, new { enabled = true, corner = "topLeft" });
        var badCorner = await _host.CallAsync("recording.setPip", JsonSerializer.Serialize(new { sessionId, enabled = true, corner = "middle" }));
        Assert.Equal("bridge.invalidParams", badCorner.GetProperty("error").GetProperty("code").GetString());
        await StopAsync(sessionId);

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal([(0L, true, "bottomRight"), (2000L, true, "topLeft")], manifest.VideoPip.Select(p => (p.AtMs, p.Enabled, p.Corner)));
        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));
        Assert.Equal("topLeft", project.GetProperty("video").GetProperty("pip").GetProperty("corner").GetString());
        Assert.Equal(2, project.GetProperty("video").GetProperty("tracks").GetArrayLength());
    }

    [Fact]
    public async Task RemovingTheVideoKeepsTheAudioAndSaysSo()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Remove me", Mic, FakeVideoCapture.Display1);
        var busy = await _host.CallAsync("video.remove", JsonSerializer.Serialize(new { recordingId }));
        Assert.Equal("project.recording", busy.GetProperty("error").GetProperty("code").GetString());
        _host.Session.Advance(TimeSpan.FromSeconds(2));
        await StopAsync(sessionId);
        await _host.WaitForStageLabelAsync(recordingId, StageNames.Video, "Done");
        // The stage publishes "Done" before it deletes joined parts and writes History; video.remove refuses with
        // library.busy until the stage has really returned, so wait for processing to be idle (audit 2, SA2-71).
        await _host.Processing.WhenIdleAsync();
        var folder = _host.Store.GetProjectFolder(recordingId);

        var freed = await _host.ResultAsync("video.remove", JsonSerializer.Serialize(new { recordingId }));

        Assert.Equal(4096, freed.GetProperty("freedBytes").GetInt64());
        Assert.False(File.Exists(Path.Combine(folder, "tracks", "screen.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "tracks", "mic.wav")) || Directory.EnumerateFiles(Path.Combine(folder, "tracks"), "mic.*").Any());
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal((ProjectVideoRemoved.ByUser, 4096L), (manifest.VideoRemoved!.Reason, manifest.VideoRemoved.BytesFreed));
        Assert.Null(Assert.Single(manifest.VideoTracks).Sha256);
        Assert.DoesNotContain("tracks/screen.mp4", manifest.Integrity.Files.Keys);
        Assert.Null(StageListOf(manifest, StageNames.Video));
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "video" && h.Summary == "Removed the video · 4 KB freed" && h.Detail!.Contains("Audio tracks, the transcript and documents are kept", StringComparison.Ordinal));
        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));
        Assert.Empty(project.GetProperty("video").GetProperty("tracks").EnumerateArray());
        Assert.Equal(4096, project.GetProperty("video").GetProperty("removed").GetProperty("bytesFreed").GetInt64());
        Assert.False(project.GetProperty("summary").GetProperty("hasVideo").GetBoolean());
        Assert.Equal(JsonValueKind.String, project.GetProperty("summary").GetProperty("video").GetProperty("removedAt").ValueKind);

        var again = await _host.CallAsync("video.remove", JsonSerializer.Serialize(new { recordingId }));
        Assert.Equal("bridge.invalidParams", again.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task VideoOlderThanTheSettingIsRemovedAndCounted()
    {
        var recordingId = await RecordWithVideoAsync();
        var usage = await _host.ResultAsync("library.usage");
        Assert.Equal((4096L, 1), (usage.GetProperty("videoBytes").GetInt64(), usage.GetProperty("videoRecordings").GetInt32()));
        Assert.Equal("""{"recordings":0,"bytes":0}""", (await _host.ResultAsync("video.removeEstimate", """{"recordingIds":null,"olderThanDays":30}""")).GetRawText());
        Assert.Equal("""{"recordings":1,"bytes":4096}""", (await _host.ResultAsync("video.removeEstimate", JsonSerializer.Serialize(new { recordingIds = new[] { recordingId }, olderThanDays = (int?)null }))).GetRawText());
        Assert.Equal("""{"recordings":1,"bytes":4096}""", (await _host.ResultAsync("video.removeEstimate", """{"recordingIds":null,"olderThanDays":null}""")).GetRawText());

        // Nothing is older than a day yet.
        await _host.ResultAsync("settings.set", """{"storage":{"removeVideoOlderThanDays":1}}""");
        Assert.Equal(0, await _host.Get<VideoRemovalService>().SweepAsync(CancellationToken.None));

        // Make the recording look 40 days old.
        await _host.Get<Memento.Core.Library.ProjectCatalog>().UpdateAsync(recordingId, m => m with { CreatedAt = m.CreatedAt.AddDays(-40) }, CancellationToken.None);
        await _host.ResultAsync("settings.set", """{"storage":{"removeVideoOlderThanDays":30}}""");
        Assert.Equal("""{"recordings":1,"bytes":4096}""", (await _host.ResultAsync("video.removeEstimate", """{"recordingIds":null,"olderThanDays":30}""")).GetRawText());

        // Never while a recording runs.
        var (sessionId, _) = await _host.StartAsync("Busy", Mic);
        Assert.Equal(0, await _host.Get<VideoRemovalService>().SweepAsync(CancellationToken.None));
        await StopAsync(sessionId);
        await _host.Processing.WhenIdleAsync();

        Assert.Equal(1, await _host.Get<VideoRemovalService>().SweepAsync(CancellationToken.None));
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(ProjectVideoRemoved.ByAge, manifest.VideoRemoved!.Reason);
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "video" && h.Detail!.StartsWith("Settings › Storage removes video older than 30 days.", StringComparison.Ordinal));
    }

    /// <summary>Records with video, checkpoints and crashes; returns the interrupted recording's id.</summary>
    private async Task<string> CrashWithVideoAsync()
    {
        var first = NewHost();
        await first.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":5}}""");
        var (_, recordingId) = await first.StartAsync("Crashed demo", Mic, FakeVideoCapture.Display1);
        first.Session.Advance(TimeSpan.FromSeconds(6));
        await first.Session.DrainAsync();
        await first.WaitForCheckpointWrittenAsync();
        await first.Session.SimulateCrashAsync(flushBufferedSamples: false);
        first.DisposeServicesOnly();
        return recordingId;
    }

    [Fact]
    public async Task AVideoFileThatCannotBeReadDoesNotStopRecovery()
    {
        // Audit 2, SA2-01: a damaged or crafted MP4 threw past recovery's narrow catch and stopped the recovery of every
        // project after it, at every launch. Now the file is kept as it is and the recording is recovered.
        var recordingId = await CrashWithVideoAsync();
        _files.RepairWith = _ => throw new ArgumentOutOfRangeException("offset");

        using var second = NewHost();
        var recovered = await second.Recovery.RunAsync(CancellationToken.None);

        Assert.Equal([recordingId], recovered);
        var manifest = await second.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal("tracks/screen.mp4", Assert.Single(Assert.Single(manifest.VideoTracks).Parts).File);
        Assert.True(File.Exists(Path.Combine(second.Store.GetProjectFolder(recordingId), "tracks", "screen.mp4")));
        await second.Processing.WhenIdleAsync();
    }

    [Fact]
    public async Task APartOpenedAfterTheLastCheckpointIsFoundOnDiskAfterACrash()
    {
        // Audit 2, SA2-03: the encoder fallback's part2 was on disk but not in recording.state.json, so it was neither
        // in the recording nor removed by "Remove video".
        var recordingId = await CrashWithVideoAsync();
        _files.RepairWith = path => path.EndsWith("part2.mp4", StringComparison.Ordinal) ? new VideoFileRepair(true, 30, 1_000, 0) : new VideoFileRepair(true, 150, 5_000, 0);

        using var second = NewHost();
        await File.WriteAllBytesAsync(Path.Combine(second.Store.GetProjectFolder(recordingId), "tracks", "screen.part2.mp4"), new byte[512]);
        await second.Recovery.RunAsync(CancellationToken.None);

        var track = Assert.Single((await second.Store.LoadAsync(recordingId, CancellationToken.None)).VideoTracks);
        Assert.Equal(["tracks/screen.mp4", "tracks/screen.part2.mp4"], track.Parts.Select(p => p.File));
        var found = track.Parts[1];
        Assert.Equal((track.Parts[0].StartOffsetMs + 5_000, 1_000L, 30L), (found.StartOffsetMs, found.DurationMs, found.FramesWritten));
        Assert.Equal(180, track.FramesWritten);
        await second.Processing.WhenIdleAsync();
    }

    [Fact]
    public async Task AnInterruptedRecordingKeepsItsVideoAndTheDialogNamesIt()
    {
        string recordingId;
        var first = NewHost();
        await first.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":5}}""");
        (_, recordingId) = await first.StartAsync("Crashed demo", Mic, FakeVideoCapture.Display1);
        first.Session.Advance(TimeSpan.FromSeconds(6));
        await first.Session.DrainAsync();
        await first.WaitForCheckpointWrittenAsync();
        await first.Session.SimulateCrashAsync(flushBufferedSamples: false);
        first.DisposeServicesOnly();

        using var second = NewHost();
        var recovered = await second.Recovery.RunAsync(CancellationToken.None);

        Assert.Equal([recordingId], recovered);
        var folder = second.Store.GetProjectFolder(recordingId);
        Assert.Contains(Path.Combine(folder, "tracks", "screen.mp4"), _files.Repaired);
        var manifest = await second.Store.LoadAsync(recordingId, CancellationToken.None);
        var track = Assert.Single(manifest.VideoTracks);
        Assert.Equal((150L, 5000L), (track.FramesWritten, track.DurationMs));
        Assert.True(manifest.HasVideo);
        var recoveredTrack = Assert.Single(manifest.Recovery!.VideoTracks!);
        Assert.Equal(("screen", "Display 1", 5000L), (recoveredTrack.TrackId, recoveredTrack.SourceName, recoveredTrack.RecoveredDurationMs));
        var item = Assert.Single((await second.ResultAsync("recovery.list")).GetProperty("items").EnumerateArray());
        Assert.Equal("""[{"trackId":"screen","sourceName":"Display 1","recoveredDurationMs":5000}]""", item.GetProperty("videoTracks").GetRawText());
        var history = await second.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "recovered" && h.Detail!.Contains("1 of 1 video track intact", StringComparison.Ordinal));
        await second.Processing.WhenIdleAsync();
    }

    [Fact]
    public async Task TheVideoStageJoinsPartsAndRemovesThemOnlyAfterTheJoinedFileIsRecorded()
    {
        var recordingId = await RecordWithVideoAsync();
        var folder = _host.Store.GetProjectFolder(recordingId);
        File.WriteAllBytes(Path.Combine(folder, "tracks", "screen.part2.mp4"), new byte[3000]);
        await _host.Get<Memento.Core.Library.ProjectCatalog>().UpdateAsync(
            recordingId,
            m => m with
            {
                VideoTracks = [m.VideoTracks[0] with { Parts = [.. m.VideoTracks[0].Parts, new VideoTrackPart("tracks/screen.part2.mp4", 6000, 4000, "Microsoft H.264 software encoder", false, 120)] }],
            },
            CancellationToken.None);
        _files.JoinReencodes = true;

        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId, stage = "video" }));
        await _host.Processing.WhenIdleAsync();

        var join = Assert.Single(_files.Joins);
        Assert.Equal([0L, 5960L], join.Select(p => p.StartMs));
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        var track = Assert.Single(manifest.VideoTracks);
        Assert.Equal(("tracks/screen.joined.mp4", "mp4"), (track.File, track.Container));
        Assert.Equal(track.Sha256, manifest.Integrity.Files["tracks/screen.joined.mp4"]);
        Assert.False(File.Exists(Path.Combine(folder, "tracks", "screen.part2.mp4")));
        Assert.False(File.Exists(Path.Combine(folder, "tracks", "screen.mp4")));
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "video" && h.Detail!.Contains("joined the screen video's 2 parts into one file (encoded again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACameraAnotherAppHoldsStartsByItselfWhenItIsFree()
    {
        _capture.Refuse[FakeVideoCapture.Camera] = FakeVideoCapture.CameraInUse();
        var (sessionId, _) = await _host.StartAsync("Camera later", Mic, FakeVideoCapture.Camera);
        await _host.Sink.WaitForAsync(BridgeEventNames.RecordingVideoProblem, p => p.GetProperty("code").GetString() == "video.cameraInUse");
        Assert.Empty(_capture.Started);

        _capture.Refuse.Remove(FakeVideoCapture.Camera);

        await WaitUntilAsync(() => _capture.Started.Count == 1, "the camera track to start once the camera is free", 10_000);
        Assert.Equal("camera", _capture.Started[0].TrackId);
        await StopAsync(sessionId);
    }

    [Fact]
    public async Task LowerQualityReachesTheRunningTracks()
    {
        var (sessionId, _) = await _host.StartAsync("Busy PC", Mic, FakeVideoCapture.Display1);

        var result = await _host.ResultAsync("recording.setVideoQuality", JsonSerializer.Serialize(new { sessionId, quality = "smaller" }));

        Assert.Equal(["smaller"], _capture.Started[0].Qualities);
        Assert.Equal("screen", result.GetProperty("videoTracks")[0].GetProperty("trackId").GetString());
        var bad = await _host.CallAsync("recording.setVideoQuality", JsonSerializer.Serialize(new { sessionId, quality = "huge" }));
        Assert.Equal("bridge.invalidParams", bad.GetProperty("error").GetProperty("code").GetString());
        await StopAsync(sessionId);
    }

    [Fact]
    public async Task AFailedVideoStageStaysOnTheProcessingCardWithItsRemediesUntilOneIsChosen()
    {
        var recordingId = await RecordWithVideoAsync();
        var folder = _host.Store.GetProjectFolder(recordingId);
        File.WriteAllBytes(Path.Combine(folder, "tracks", "screen.part2.mp4"), new byte[3000]);
        await _host.Get<Memento.Core.Library.ProjectCatalog>().UpdateAsync(
            recordingId,
            m => m with { VideoTracks = [m.VideoTracks[0] with { Parts = [.. m.VideoTracks[0].Parts, new VideoTrackPart("tracks/screen.part2.mp4", 6000, 4000, "Microsoft H.264 software encoder", false, 120)] }] },
            CancellationToken.None);
        _files.FailNextJoin = new IOException("The disk is full");

        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId, stage = "video" }));
        await _host.Processing.WhenIdleAsync();

        var card = (await _host.ResultAsync("library.processing")).GetProperty("current");
        Assert.Equal(recordingId, card.GetProperty("recordingId").GetString());
        var failure = card.GetProperty("failure");
        Assert.Equal("video", failure.GetProperty("stage").GetString());
        Assert.StartsWith("Finalising the video file stopped at", failure.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(["software", "keepRaw"], failure.GetProperty("remedies").EnumerateArray().Select(r => r.GetProperty("id").GetString()));

        // Keep raw frames: the parts stay as written, each hashed, and the stage is done.
        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId, stage = "video", remedyId = "keepRaw" }));
        await _host.Processing.WhenIdleAsync();

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal("done", manifest.Stages.Single(s => s.Stage == "video").State);
        Assert.Equal("tracks/screen.mp4", manifest.VideoTracks[0].File);
        Assert.True(File.Exists(Path.Combine(folder, "tracks", "screen.part2.mp4")));
        Assert.Contains("tracks/screen.part2.mp4", manifest.Integrity.Files.Keys);
        Assert.False(manifest.Processing?.VideoKeepRaw ?? false);
        Assert.Equal(JsonValueKind.Null, (await _host.ResultAsync("library.processing")).GetProperty("current").ValueKind);
        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));
        Assert.Equal(2, project.GetProperty("video").GetProperty("tracks")[0].GetProperty("parts").GetArrayLength());

        // Software: the parts are joined by encoding them again.
        await _host.ResultAsync("processing.retry", JsonSerializer.Serialize(new { recordingId, stage = "video", remedyId = "software" }));
        await _host.Processing.WhenIdleAsync();
        Assert.True(_files.LastJoinReencoded);
    }

    private async Task<string> RecordWithVideoAsync()
    {
        var (sessionId, recordingId) = await _host.StartAsync("With video", Mic, FakeVideoCapture.Display1);
        _host.Session.Advance(TimeSpan.FromSeconds(2));
        await StopAsync(sessionId);
        await _host.WaitForStageLabelAsync(recordingId, StageNames.Video, "Done");
        await _host.Processing.WhenIdleAsync();
        return recordingId;
    }

    private static Memento.Core.Bridge.Contracts.StageStatus? StageListOf(ProjectManifest manifest, string stage) => manifest.Stages.FirstOrDefault(s => s.Stage == stage);
}
