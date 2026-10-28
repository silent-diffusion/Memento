using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Memento.Core.Video;
using Microsoft.Extensions.DependencyInjection;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Video;

/// <summary>
/// 2.8.0: <c>video.addSource</c> and <c>video.removeSource</c> start and stop a screen while recording. Each track keeps
/// its own time range; audio is never touched. Capture is <see cref="FakeVideoCapture"/>; the audio engine runs in manual time.
/// </summary>
public sealed class VideoAddRemoveSourceTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeVideoCapture _capture = new();
    private readonly FakeVideoFiles _files = new();
    private readonly BridgeTestHost _host;

    public VideoAddRemoveSourceTests()
    {
        _host = new BridgeTestHost(directory: _directory, configure: services =>
        {
            services.AddSingleton<IVideoCapture>(_capture);
            services.AddSingleton<IVideoFiles>(_files);
        });
    }

    public void Dispose()
    {
        _host.Dispose();
        _directory.Dispose();
    }

    private Task<JsonElement> AddAsync(string sessionId, string sourceId) =>
        _host.CallAsync("video.addSource", JsonSerializer.Serialize(new { sessionId, sourceId }));

    private Task<JsonElement> RemoveAsync(string sessionId, string trackId) =>
        _host.CallAsync("video.removeSource", JsonSerializer.Serialize(new { sessionId, trackId }));

    private static string[] TrackIds(JsonElement response) =>
        response.GetProperty("result").GetProperty("videoTracks").EnumerateArray().Select(t => t.GetProperty("trackId").GetString()!).ToArray();

    private async Task StopAsync(string sessionId)
    {
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _host.Recordings.WhenIdleAsync();
    }

    [Fact]
    public async Task AScreenAddedWhileRecordingIsATrackOfItsOwnWithItsOwnTimeRange()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Two screens", Mic, FakeVideoCapture.Display1);
        _host.Session.Advance(TimeSpan.FromSeconds(4));

        var added = await AddAsync(sessionId, FakeVideoCapture.Window);

        Assert.Equal(["screen", "screen-2"], TrackIds(added));
        var second = _capture.Started[1];
        Assert.Equal(("screen-2", "screen", FakeVideoCapture.Window), (second.TrackId, second.Request.Role, second.Request.SourceId));
        Assert.Equal(_host.Session.TimelineOriginQpc, second.Request.TimelineOriginQpc);
        var listed = added.GetProperty("result").GetProperty("videoTracks")[1];
        Assert.Equal("screen", listed.GetProperty("role").GetString());

        // Pause and resume reach both screens.
        await _host.ResultAsync("recording.pause", JsonSerializer.Serialize(new { sessionId }));
        await _host.ResultAsync("recording.resume", JsonSerializer.Serialize(new { sessionId }));
        Assert.All(_capture.Started, t => Assert.Equal((1, 1), (t.Pauses, t.Resumes)));

        // Removing the added screen stops only it, from now.
        _host.Session.Advance(TimeSpan.FromSeconds(3));
        var removed = await RemoveAsync(sessionId, "screen-2");
        Assert.Equal(["screen"], TrackIds(removed));
        Assert.True(second.Stopped);
        Assert.False(_capture.Started[0].Stopped);

        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await StopAsync(sessionId);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(ProjectStates.Ready, manifest.State);
        var extra = manifest.VideoTracks.Single(t => t.Id == "screen-2");
        Assert.Equal(("disabled", 7000L, 7000L), (extra.EndReason, extra.EndedEarlyAtMs!.Value, extra.StoppedAtMs!.Value));
        var first = manifest.VideoTracks.Single(t => t.Id == "screen");
        Assert.Equal(10_040L, first.StoppedAtMs);

        // Review gets both screens with their own ids and ranges.
        var project = await _host.ResultAsync("project.get", JsonSerializer.Serialize(new { recordingId }));
        var tracks = project.GetProperty("video").GetProperty("tracks").EnumerateArray().ToList();
        Assert.Equal(["screen", "screen-2"], tracks.Select(t => t.GetProperty("trackId").GetString()));
        Assert.All(tracks, t => Assert.Equal("screen", t.GetProperty("id").GetString()));
        Assert.Equal(7000L, tracks[1].GetProperty("stoppedAtMs").GetInt64());
    }

    [Fact]
    public async Task RemovingTheLastScreenKeepsTheAudioRecordingAndAScreenCanBeAddedAgain()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Screen off", Mic, FakeVideoCapture.Display1);

        Assert.Empty(TrackIds(await RemoveAsync(sessionId, "screen")));
        Assert.Equal("recording", (await _host.ResultAsync("recording.current")).GetProperty("session").GetProperty("state").GetString());

        // With no screen running the added one is the screen track again (a new file).
        Assert.Equal(["screen"], TrackIds(await AddAsync(sessionId, FakeVideoCapture.Display2)));
        Assert.Equal(["screen", "screen-2"], _capture.Started.Select(t => t.TrackId));

        await StopAsync(sessionId);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(["screen", "screen-2"], manifest.VideoTracks.Select(t => t.Id).Order(StringComparer.Ordinal));
        Assert.True(manifest.Tracks.Count > 0);
    }

    [Fact]
    public async Task AddingIsValidatedAndNothingChangesWhenItIsRefused()
    {
        var (sessionId, _) = await _host.StartAsync("Checks", Mic, FakeVideoCapture.Display1);

        var camera = await AddAsync(sessionId, FakeVideoCapture.Camera);
        Assert.Equal("bridge.invalidParams", camera.GetProperty("error").GetProperty("code").GetString());

        var twice = await AddAsync(sessionId, FakeVideoCapture.Display1);
        Assert.Equal("video.sourceAlreadyRecorded", twice.GetProperty("error").GetProperty("code").GetString());
        Assert.StartsWith("Display 1 is already being recorded. Nothing changed", twice.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);

        var empty = await AddAsync(sessionId, " ");
        Assert.Equal("bridge.invalidParams", empty.GetProperty("error").GetProperty("code").GetString());

        _capture.Refuse[FakeVideoCapture.Window] = new VideoStartException(DomainErrorCodes.VideoProtectedContent, "“Planning notes” shows protected content.");
        var refused = await AddAsync(sessionId, FakeVideoCapture.Window);
        Assert.Equal("video.protectedContent", refused.GetProperty("error").GetProperty("code").GetString());

        var unknown = await RemoveAsync(sessionId, "screen-9");
        Assert.Equal("video.sourceNotFound", unknown.GetProperty("error").GetProperty("code").GetString());

        var noSession = await AddAsync("nope", FakeVideoCapture.Display2);
        Assert.Equal("recording.noSession", noSession.GetProperty("error").GetProperty("code").GetString());

        Assert.Single(_capture.Started);
        await StopAsync(sessionId);
    }

    [Fact]
    public async Task NoMoreThanTheMostScreensRecordAtOnce()
    {
        _capture.MoreWindows.Add(new Memento.Core.Bridge.Contracts.VideoSource("window:B1", "window", "Second window", "App · 800 × 600", 800, 600, 0, false, false, false, "App", null));
        var (sessionId, _) = await _host.StartAsync("Many", Mic, FakeVideoCapture.Display1);
        await AddAsync(sessionId, FakeVideoCapture.Display2);
        await AddAsync(sessionId, FakeVideoCapture.Window);

        var fifth = await AddAsync(sessionId, "window:B1");
        Assert.True(fifth.TryGetProperty("result", out _));
        var tooMany = await AddAsync(sessionId, "window:C1");
        Assert.Equal("video.tooManySources", tooMany.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(4, _capture.Started.Count);
        await StopAsync(sessionId);
    }

    [Fact]
    public async Task AProblemOfAnAddedScreenIsToldWithItsOwnTrackId()
    {
        var (sessionId, _) = await _host.StartAsync("Window closes", Mic, FakeVideoCapture.Display1);
        await AddAsync(sessionId, FakeVideoCapture.Window);

        _capture.Started[1].Raise("ended", "video.windowClosed", "“Planning notes” was closed at 0:00:05.", 5000);

        var problem = await _host.Sink.WaitForAsync(BridgeEventNames.RecordingVideoProblem, p => p.GetProperty("code").GetString() == "video.windowClosed");
        Assert.Equal("screen-2", problem.GetProperty("trackId").GetString());
        await StopAsync(sessionId);
    }
}
