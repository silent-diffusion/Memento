using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Projects;
using Memento.Core.Status;
using Memento.Core.Tests.Fakes;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Recording;

/// <summary>The whole recording pipeline through the bridge, on the simulated engine in manual time.</summary>
public sealed class RecordingPipelineTests : IDisposable
{
    private static readonly string[] TwoPeople = ["Avery", "Rowan"];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string? ErrorCode(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private static string ErrorMessage(JsonElement response) => response.GetProperty("error").GetProperty("message").GetString()!;

    private Task<JsonElement> Session(string method, string sessionId, object? extra = null)
    {
        var parameters = extra is null
            ? JsonSerializer.Serialize(new { sessionId })
            : JsonSerializer.Serialize(new Dictionary<string, object?>(JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(extra))!) { ["sessionId"] = sessionId });
        return _host.ResultAsync(method, parameters);
    }

    private async Task UseCheckpointSecondsAsync(int seconds) =>
        await _host.ResultAsync("settings.set", "{\"recording\":{\"checkpointSeconds\":" + seconds + "}}");

    [Fact]
    public async Task AFullSessionIsCapturedCheckpointedAndFinalized()
    {
        await UseCheckpointSecondsAsync(5);
        var (sessionId, recordingId) = await _host.StartAsync("Weekly sync", Mic, SystemAudio);
        var session = _host.Session;
        var folder = _host.Store.GetProjectFolder(recordingId);

        Assert.True(_host.Store.HasRecordingState(recordingId));
        Assert.Equal("recording", (await _host.ResultAsync("recording.current")).GetProperty("session").GetProperty("state").GetString());

        // Checkpoint at 5 s: headers patched, recording.state.json updated.
        session.Advance(TimeSpan.FromSeconds(6));
        await session.DrainAsync();
        await WaitUntilAsync(
            async () => (await _host.Store.ReadRecordingStateAsync(recordingId, CancellationToken.None))?.LastCheckpointAt is not null,
            "the checkpoint in recording.state.json");
        var state = await _host.Store.ReadRecordingStateAsync(recordingId, CancellationToken.None);
        Assert.All(state!.Tracks, t => Assert.True(t.BytesAtCheckpoint > 0));
        Assert.True(WavInfo.Read(Path.Combine(folder, "tracks", "mic.wav")).DeclaredDataBytes >= 5 * 96_000);

        // Pause for 2 s: no audio written, the gap is recorded.
        await Session("recording.pause", sessionId);
        session.Advance(TimeSpan.FromSeconds(2));
        await Session("recording.resume", sessionId);
        session.Advance(TimeSpan.FromSeconds(1));

        var highlight = (await Session("recording.markHighlight", sessionId, new { note = "Decision on hiring" })).GetProperty("highlight");
        Assert.Equal(7000, highlight.GetProperty("atMs").GetInt64());

        var tracks = (await Session("recording.setSource", sessionId, new { sourceId = SystemAudio, enabled = false })).GetProperty("tracks");
        Assert.Equal(7000, tracks.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "system").GetProperty("endedEarlyAtMs").GetInt64());

        session.Advance(TimeSpan.FromSeconds(1));
        var stopped = await Session("recording.stop", sessionId);
        Assert.Equal(recordingId, stopped.GetProperty("recordingId").GetString());
        await _host.Recordings.WhenIdleAsync();

        // Manifest.
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.Equal(8000, manifest.DurationMs);
        var pause = Assert.Single(manifest.Pauses);
        Assert.Equal(6000, pause.AtMs);
        Assert.Equal(2000, pause.DurationMs);
        var mic = manifest.Tracks.Single(t => t.Id == "mic");
        var system = manifest.Tracks.Single(t => t.Id == "system");
        Assert.Equal(8000, mic.DurationMs);
        Assert.Equal(7000, system.DurationMs);
        Assert.Equal(7000, system.EndedEarlyAtMs);
        Assert.Equal("disabled", system.EndReason);
        Assert.Equal(2, system.Channels);
        Assert.Equal("stored", Assert.Single(manifest.Stages).Stage);
        Assert.Equal("done", manifest.Stages[0].State);

        // Files and hashes: no FLAC encoder in Core, so tracks stay WAV and the mix is mix.wav.
        Assert.Equal("tracks/mic.wav", mic.File);
        Assert.Equal("wav", mic.Codec);
        Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, "tracks", "mic.wav"), CancellationToken.None), mic.Sha256);
        Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, "tracks", "system.wav"), CancellationToken.None), system.Sha256);
        Assert.Equal("mix.wav", manifest.Mix!.File);
        Assert.Equal(8000, manifest.Mix.DurationMs);
        Assert.Equal(2, manifest.Mix.Channels);
        Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, "mix.wav"), CancellationToken.None), manifest.Mix.Sha256);
        Assert.Equal(["mix.wav", "tracks/mic.wav", "tracks/system.wav"], manifest.Integrity.Files.Keys.Order(StringComparer.Ordinal));
        Assert.NotNull(manifest.Integrity.ComputedAt);
        Assert.True(File.GetAttributes(Path.Combine(folder, "tracks", "mic.wav")).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.GetAttributes(Path.Combine(folder, "mix.wav")).HasFlag(FileAttributes.ReadOnly));
        Assert.False(File.Exists(Path.Combine(folder, "recording.state.json")));
        Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp*", SearchOption.AllDirectories));

        using (var peaks = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "peaks.json"))))
        {
            Assert.Equal(1, peaks.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(50, peaks.RootElement.GetProperty("windowMs").GetInt32());
            Assert.Equal(2 * 160, peaks.RootElement.GetProperty("peaks").GetArrayLength());
        }

        // Annotations and history.
        var annotations = await _host.Store.LoadAnnotationsAsync(recordingId, CancellationToken.None);
        Assert.Equal("Decision on hiring", Assert.Single(annotations.Highlights).Note);
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Equal(
            ["recorded/started", "recorded/completed", "stored/started", "stored/completed"],
            history.Select(h => $"{h.Stage}/{h.Event}"));
        Assert.Equal("Recorded 0:08", history[1].Summary);
        Assert.Contains("paused 1 time", history[1].Detail, StringComparison.Ordinal);
        Assert.Contains("Kept as WAV", history[3].Detail, StringComparison.Ordinal);
        Assert.StartsWith("Stored 2 tracks · ", history[3].Summary, StringComparison.Ordinal);

        // Events.
        var states = _host.Sink.Payloads(BridgeEventNames.RecordingState).Select(p => p.GetProperty("state").GetString()).Distinct().ToList();
        Assert.Equal(["recording", "paused", "finalizing", "ready"], states);
        var progress = _host.Sink.Payloads(BridgeEventNames.ProcessingProgress).Select(p => p.GetProperty("stages")[0].GetProperty("state").GetString()).ToList();
        Assert.Equal("active", progress[0]);
        Assert.Equal("done", progress[^1]);
        Assert.Contains(_host.Sink.Payloads(BridgeEventNames.LibraryChanged), p => p.GetProperty("recordingIds")[0].GetString() == recordingId);
        var ready = _host.Sink.Payloads(BridgeEventNames.RecordingState).Last();
        Assert.Equal("ready", ready.GetProperty("state").GetString());
        Assert.Equal(1, ready.GetProperty("highlightsCount").GetInt32());
        Assert.Equal(8000, ready.GetProperty("elapsedMs").GetInt64());
        Assert.All(ready.GetProperty("tracks").EnumerateArray(), t => Assert.Equal(64, t.GetProperty("sha256").GetString()!.Length));

        // Library and rejoin.
        var list = await _host.ResultAsync("library.list");
        var row = Assert.Single(list.GetProperty("recordings").EnumerateArray());
        Assert.Equal("Weekly sync", row.GetProperty("title").GetString());
        Assert.Equal("ready", row.GetProperty("state").GetString());
        Assert.Equal(8000, row.GetProperty("durationMs").GetInt64());
        Assert.Equal(0, row.GetProperty("stages").GetArrayLength());
        Assert.Equal(_host.Store.GetSizeBytes(recordingId), row.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, (await _host.ResultAsync("recording.current")).GetProperty("session").ValueKind);
        Assert.Equal([Mic, SystemAudio], _host.Settings.Current.Recording.DefaultSourceIds);
        Assert.Equal(0, _host.Session.Overruns);
    }

    [Fact]
    public async Task ProjectGetReturnsPlayableUrlsAfterFinalize()
    {
        var recordingId = await _host.RecordAsync("Short", 1);

        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));

        Assert.Equal($"https://library.memento/projects/{recordingId}/mix.wav", project.GetProperty("mixUrl").GetString());
        Assert.Equal($"https://library.memento/projects/{recordingId}/peaks.json", project.GetProperty("peaksUrl").GetString());
        Assert.Equal("tracks/mic.wav", project.GetProperty("tracks")[0].GetProperty("file").GetString());
        Assert.Equal(4, project.GetProperty("history").GetArrayLength());
        Assert.True(project.GetProperty("sizeBytes").GetInt64() > 96_000);
        Assert.Equal("sha256", project.GetProperty("integrity").GetProperty("algorithm").GetString());
    }

    [Fact]
    public async Task MixUrlIsNullWhileRecording()
    {
        var (_, recordingId) = await _host.StartAsync("Live", Mic);

        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));

        Assert.Equal(JsonValueKind.Null, project.GetProperty("mixUrl").ValueKind);
        Assert.Equal("recording", project.GetProperty("summary").GetProperty("state").GetString());
        Assert.Equal("tracks/mic.wav", project.GetProperty("tracks")[0].GetProperty("file").GetString());
    }

    [Fact]
    public async Task LevelsArriveForEveryOpenSource()
    {
        var (sessionId, _) = await _host.StartAsync("Levels", Mic, SystemAudio);

        _host.Session.Advance(TimeSpan.FromMilliseconds(200));
        var levels = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingLevels);

        Assert.Equal(sessionId, levels.GetProperty("sessionId").GetString());
        var readings = levels.GetProperty("levels").EnumerateArray().ToList();
        Assert.Equal([Mic, SystemAudio], readings.Select(r => r.GetProperty("sourceId").GetString()));
        Assert.All(readings, r => Assert.InRange(r.GetProperty("peak").GetDouble(), 0.01, 1));
        Assert.All(readings, r => Assert.InRange(r.GetProperty("rms").GetDouble(), 0.0001, 1));
    }

    [Fact]
    public async Task ATurnedOnSourceStartsANewTrackAtTheCurrentTime()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Late join", Mic);
        _host.Session.Advance(TimeSpan.FromSeconds(1));

        await Session("recording.setSource", sessionId, new { sourceId = App, enabled = true });
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await Session("recording.setSource", sessionId, new { sourceId = Mic, enabled = false });
        await Session("recording.setSource", sessionId, new { sourceId = Mic, enabled = true });
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await Session("recording.stop", sessionId);
        await _host.Recordings.WhenIdleAsync();

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(["mic", "app-simulated-meeting-app", "mic-2"], manifest.Tracks.Select(t => t.Id));
        Assert.Equal(1000, manifest.Tracks[1].StartOffsetMs);
        Assert.Equal(2000, manifest.Tracks[1].DurationMs);
        Assert.Equal(2000, manifest.Tracks[0].EndedEarlyAtMs);
        Assert.Equal(2000, manifest.Tracks[2].StartOffsetMs);
        Assert.Equal(3000, manifest.Mix!.DurationMs);
    }

    [Fact]
    public async Task ALostSourceEndsOnlyItsTrack()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Unplugged", Mic, SystemAudio);
        _host.Session.Advance(TimeSpan.FromSeconds(1));

        await _host.Session.SimulateSourceLostAsync(SystemAudio);
        var lost = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingSourceLost);
        _host.Session.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(SystemAudio, lost.GetProperty("sourceId").GetString());
        Assert.Equal("Simulated system audio", lost.GetProperty("name").GetString());
        Assert.Equal(1000, lost.GetProperty("atMs").GetInt64());
        Assert.Equal(["Simulated microphone"], lost.GetProperty("remaining").EnumerateArray().Select(e => e.GetString()));
        await WaitUntilAsync(() => _host.Get<FooterStatusService>().Compute().Recording.LostSource == "Simulated system audio", "the footer lost-source line");

        await Session("recording.stop", sessionId);
        await _host.Recordings.WhenIdleAsync();
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal("sourceLost", manifest.Tracks.Single(t => t.Id == "system").EndReason);
        Assert.Equal(2000, manifest.Tracks.Single(t => t.Id == "mic").DurationMs);
        Assert.Equal(ProjectStates.Ready, manifest.State);
    }

    [Fact]
    public async Task LosingEverySourceStopsTheRecordingAndKeepsIt()
    {
        var (_, recordingId) = await _host.StartAsync("Gone", Mic);
        _host.Session.Advance(TimeSpan.FromSeconds(1));

        await _host.Session.SimulateSourceLostAsync(Mic);
        var stopped = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingStoppedByHost);
        await _host.Recordings.WhenIdleAsync();

        Assert.Equal("deviceLost", stopped.GetProperty("reason").GetString());
        Assert.Equal(ProjectStates.Ready, (await _host.Store.LoadAsync(recordingId, CancellationToken.None)).State);
    }

    [Fact]
    public async Task RunningOutOfSpaceStopsCleanlyAndReportsTheTime()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Big one", Mic, SystemAudio);
        _host.Session.Advance(TimeSpan.FromSeconds(62));

        _host.FreeSpace.FreeBytes = 100L * 1024 * 1024;
        var stopped = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingStoppedByHost);
        await _host.Recordings.WhenIdleAsync();

        Assert.Equal(sessionId, stopped.GetProperty("sessionId").GetString());
        Assert.Equal(recordingId, stopped.GetProperty("recordingId").GetString());
        Assert.Equal("diskFull", stopped.GetProperty("reason").GetString());
        Assert.Equal(62_000, stopped.GetProperty("atMs").GetInt64());
        Assert.StartsWith("Stopped · drive full at 1:02. Everything up to that point is saved", stopped.GetProperty("message").GetString(), StringComparison.Ordinal);

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        Assert.Equal(62_000, manifest.DurationMs);
        Assert.All(manifest.Tracks, t => Assert.Equal(62_000, t.DurationMs));
        var history = await _host.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Stopped · drive full at 1:02");
        var noSession = await _host.CallAsync("recording.pause", JsonSerializer.Serialize(new { sessionId }));
        Assert.Equal(DomainErrorCodes.RecordingNoSession, ErrorCode(noSession));
    }

    [Fact]
    public async Task AWriteFailingForLackOfSpaceStopsTheSession()
    {
        var (_, recordingId) = await _host.StartAsync("Full", Mic);
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await _host.Session.CheckpointAsync(CancellationToken.None);

        _host.Session.SimulateDiskFull();
        _host.Session.Advance(TimeSpan.FromMilliseconds(500));
        var stopped = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingStoppedByHost);
        await _host.Recordings.WhenIdleAsync();

        Assert.Equal("diskFull", stopped.GetProperty("reason").GetString());
        Assert.Equal(1000, stopped.GetProperty("atMs").GetInt64());
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(1000, Assert.Single(manifest.Tracks).DurationMs);
        Assert.Equal(ProjectStates.Ready, manifest.State);
    }

    [Fact]
    public async Task LowSpaceWarnsOnceAndRecordingContinues()
    {
        _host.FreeSpace.FreeBytes = 5L * 1024 * 1024 * 1024;
        var (sessionId, _) = await _host.StartAsync("Tight", Mic);

        var warning = await _host.Sink.WaitForAsync(BridgeEventNames.StorageLowSpace);
        await Task.Delay(100);

        Assert.Equal(5L * 1024 * 1024 * 1024, warning.GetProperty("freeBytes").GetInt64());
        Assert.Equal(10L * 1024 * 1024 * 1024, warning.GetProperty("thresholdBytes").GetInt64());
        Assert.True(warning.GetProperty("recordingContinues").GetBoolean());
        Assert.True(warning.GetProperty("transcriptionPaused").GetBoolean());
        Assert.Single(_host.Sink.Payloads(BridgeEventNames.StorageLowSpace));
        Assert.Equal("Low disk space", (await _host.ResultAsync("status.get")).GetProperty("processingPaused").GetString());
        Assert.Equal("recording", (await _host.ResultAsync("recording.current")).GetProperty("session").GetProperty("state").GetString());
        await Session("recording.stop", sessionId);
    }

    [Fact]
    public async Task StateIsSentRegularlyWhileRecording()
    {
        await _host.StartAsync("Ticking", Mic);
        _host.Session.Advance(TimeSpan.FromSeconds(3));

        await WaitUntilAsync(
            () => _host.Sink.Payloads(BridgeEventNames.RecordingState) is { Count: >= 4 } states
                && states[^1].GetProperty("tracks")[0].GetProperty("durationMs").GetInt64() == 3000,
            "periodic recording.state events");
        var last = _host.Sink.Payloads(BridgeEventNames.RecordingState)[^1];
        Assert.Equal(3000, last.GetProperty("elapsedMs").GetInt64());
        Assert.Equal(3000, last.GetProperty("tracks")[0].GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public async Task ASecondRecordingIsRefused()
    {
        await _host.StartAsync("First", Mic);

        var response = await _host.CallAsync("recording.start", JsonSerializer.Serialize(new { title = "Second", type = "meeting", sourceIds = new[] { SystemAudio } }));

        Assert.Equal(DomainErrorCodes.RecordingAlreadyActive, ErrorCode(response));
        Assert.StartsWith("\"First\" is still recording", ErrorMessage(response), StringComparison.Ordinal);
        Assert.Single(_host.Store.ListIds());
    }

    [Fact]
    public async Task StartNeedsASource()
    {
        var response = await _host.CallAsync("recording.start", """{"title":"x","type":"meeting","sourceIds":[]}""");

        Assert.Equal(DomainErrorCodes.RecordingNoSources, ErrorCode(response));
        Assert.Equal("Choose at least one audio source to record. Nothing was started.", ErrorMessage(response));
    }

    [Fact]
    public async Task AnUnavailableSourceStartsNothing()
    {
        _host.Sources.SetAvailable(SystemAudio, false);

        var response = await _host.CallAsync("recording.start", JsonSerializer.Serialize(new { title = "x", type = "meeting", sourceIds = new[] { Mic, SystemAudio } }));

        Assert.Equal(DomainErrorCodes.RecordingSourceUnavailable, ErrorCode(response));
        Assert.Equal(SystemAudio, response.GetProperty("error").GetProperty("detail").GetString());
        Assert.Contains("no other source was started", ErrorMessage(response), StringComparison.Ordinal);
        Assert.Null(_host.Engine.LastSession);
        Assert.Empty(_host.Store.ListIds());
    }

    [Fact]
    public async Task AnEmptyTitleBecomesUntitledType()
    {
        var result = await _host.ResultAsync("recording.start", JsonSerializer.Serialize(new { title = "  ", type = "interview", sourceIds = new[] { Mic } }));

        var manifest = await _host.Store.LoadAsync(result.GetProperty("recordingId").GetString()!, CancellationToken.None);
        Assert.Equal("Untitled interview", manifest.Details.Title);
        Assert.Equal("interview", manifest.Details.Type);
    }

    [Fact]
    public async Task StartRefusesWhenTheDriveIsAlreadyFull()
    {
        _host.FreeSpace.FreeBytes = 10L * 1024 * 1024;

        var response = await _host.CallAsync("recording.start", JsonSerializer.Serialize(new { title = "x", type = "meeting", sourceIds = new[] { Mic } }));

        Assert.Equal(DomainErrorCodes.RecordingDiskFull, ErrorCode(response));
        Assert.StartsWith("The library drive has only 10 MB free", ErrorMessage(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("recording.pause", "{\"sessionId\":\"nope\"}")]
    [InlineData("recording.resume", "{\"sessionId\":\"nope\"}")]
    [InlineData("recording.stop", "{\"sessionId\":\"nope\"}")]
    [InlineData("recording.markHighlight", "{\"sessionId\":\"nope\"}")]
    [InlineData("recording.setSource", "{\"sessionId\":\"nope\",\"sourceId\":\"x\",\"enabled\":true}")]
    public async Task SessionMethodsNeedTheActiveSession(string method, string parameters)
    {
        await _host.StartAsync("Running", Mic);

        var response = await _host.CallAsync(method, parameters);

        Assert.Equal(DomainErrorCodes.RecordingNoSession, ErrorCode(response));
        Assert.Equal("That recording session has already ended. Anything it recorded is saved in the Library.", ErrorMessage(response));
    }

    [Fact]
    public async Task TurningOnAMissingSourceKeepsTheOthersRecording()
    {
        var (sessionId, _) = await _host.StartAsync("Partial", Mic);
        _host.Sources.SetAvailable(App, false);

        var response = await _host.CallAsync("recording.setSource", JsonSerializer.Serialize(new { sessionId, sourceId = App, enabled = true }));

        Assert.Equal(DomainErrorCodes.RecordingSourceUnavailable, ErrorCode(response));
        Assert.Contains("The other tracks keep recording", ErrorMessage(response), StringComparison.Ordinal);
        Assert.Equal("recording", (await _host.ResultAsync("recording.current")).GetProperty("session").GetProperty("state").GetString());
    }

    [Fact]
    public async Task DetailsCanBeEditedWhileRecordingButTheProjectCannotBeDeleted()
    {
        var (_, recordingId) = await _host.StartAsync("In progress", Mic);

        var project = await _host.ResultAsync("project.updateDetails", JsonSerializer.Serialize(new { recordingId, details = new { participants = TwoPeople } }));
        var delete = await _host.CallAsync("project.delete", JsonSerializer.Serialize(new { recordingId }));

        Assert.Equal(2, project.GetProperty("summary").GetProperty("participantCount").GetInt32());
        Assert.Equal(DomainErrorCodes.ProjectRecording, ErrorCode(delete));
        Assert.StartsWith("\"In progress\" is still recording or being saved", ErrorMessage(delete), StringComparison.Ordinal);
        Assert.True(_host.Store.Exists(recordingId));
    }

    [Fact]
    public async Task RecordingCurrentRejoinsTheSession()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Rejoin", Mic, SystemAudio);
        _host.Session.Advance(TimeSpan.FromSeconds(2));
        await Session("recording.markHighlight", sessionId);

        var current = (await _host.ResultAsync("recording.current")).GetProperty("session");

        Assert.Equal(sessionId, current.GetProperty("sessionId").GetString());
        Assert.Equal(recordingId, current.GetProperty("recordingId").GetString());
        Assert.Equal(2000, current.GetProperty("elapsedMs").GetInt64());
        Assert.Equal(2, current.GetProperty("tracks").GetArrayLength());
        Assert.Equal(1, current.GetProperty("highlightsCount").GetInt32());
    }

    [Fact]
    public async Task TheFooterShowsTheActiveRecording()
    {
        await UseCheckpointSecondsAsync(5);
        await _host.StartAsync("Footer", Mic);
        _host.Session.Advance(TimeSpan.FromSeconds(5));
        await _host.Session.DrainAsync();

        await WaitUntilAsync(() => _host.Get<FooterStatusService>().Compute().Recording.LastCheckpointAt is not null, "the footer checkpoint time");
        var footer = await _host.ResultAsync("status.get");

        Assert.True(footer.GetProperty("recording").GetProperty("active").GetBoolean());
        Assert.Contains(_host.Sink.Payloads(BridgeEventNames.FooterStatus), p => p.GetProperty("recording").GetProperty("active").GetBoolean());
    }
}
