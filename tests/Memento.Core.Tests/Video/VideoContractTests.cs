using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Video;

/// <summary>The video contract's JSON, exactly (BRIDGE.md, Video (2.0)): methods, events, settings and refusals.</summary>
public sealed class VideoContractTests : IDisposable
{
    private readonly FakeVideoCapture _capture = new();
    private readonly BridgeTestHost _host;

    public VideoContractTests()
    {
        _host = new BridgeTestHost(configure: services => services.AddSingleton<IVideoCapture>(_capture));
    }

    public void Dispose() => _host.Dispose();

    private static string Error(JsonElement response) => response.GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task ListSourcesHasEveryFieldOfASource()
    {
        var result = await _host.ResultAsync("video.listSources");

        Assert.Equal("displays,windows,cameras", string.Join(",", result.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(
            """{"id":"window:1A2B3C","kind":"window","name":"Planning notes","detail":"Notes \u00B7 1280 \u00D7 720","width":1280,"height":720,"refreshRate":0,"primary":false,"excluded":false,"protected":false,"processName":"Notes"}""",
            result.GetProperty("windows")[0].GetRawText());
        Assert.Equal(2, result.GetProperty("displays").GetArrayLength());
        Assert.True((await _host.ResultAsync("sources.list")).GetProperty("videoAvailable").GetBoolean());
    }

    [Fact]
    public async Task SourcesListSaysVideoIsUnavailableWithoutCapture()
    {
        using var plain = new BridgeTestHost();

        Assert.False((await plain.ResultAsync("sources.list")).GetProperty("videoAvailable").GetBoolean());
        Assert.Equal("""{"displays":[],"windows":[],"cameras":[]}""", (await plain.ResultAsync("video.listSources")).GetRawText());
        var (sessionId, _) = await plain.StartAsync("Audio only", TestRecordings.Mic);
        var refused = await plain.CallAsync("recording.setVideoSource", JsonSerializer.Serialize(new { sessionId, role = "screen", sourceId = FakeVideoCapture.Display1 }));
        Assert.Equal("video.unavailable", Error(refused));
    }

    [Fact]
    public async Task AThumbnailIsADataUrlWithinLimitsAndRefusalsKeepTheirCode()
    {
        var thumbnail = await _host.ResultAsync("video.thumbnail", JsonSerializer.Serialize(new { sourceId = FakeVideoCapture.Display1 }));
        Assert.Equal("""{"dataUrl":"data:image/jpeg;base64,AAAA","width":320,"height":180}""", thumbnail.GetRawText());

        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("video.thumbnail", JsonSerializer.Serialize(new { sourceId = FakeVideoCapture.Display1, width = 4000 }))));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("video.thumbnail", JsonSerializer.Serialize(new { sourceId = "mic:x" }))));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("video.thumbnail", JsonSerializer.Serialize(new { sourceId = FakeVideoCapture.Display1, path = "C:\\x" }))));
        _capture.Refuse[FakeVideoCapture.Window] = new VideoStartException(DomainErrorCodes.VideoProtectedContent, "“Player” shows protected content.");
        var refused = await _host.CallAsync("video.thumbnail", JsonSerializer.Serialize(new { sourceId = FakeVideoCapture.Window }));
        Assert.Equal("video.protectedContent", Error(refused));
        Assert.Equal(FakeVideoCapture.Window, refused.GetProperty("error").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SessionMethodsCheckTheSessionTheRoleAndTheirFields()
    {
        Assert.Equal("recording.noSession", Error(await _host.CallAsync("recording.setVideoSource", """{"sessionId":"nope","role":"screen","sourceId":null}""")));
        Assert.Equal("recording.noSession", Error(await _host.CallAsync("recording.setPip", """{"sessionId":"nope","enabled":true}""")));
        var (sessionId, _) = await _host.StartAsync("Session", TestRecordings.Mic);
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("recording.setVideoSource", JsonSerializer.Serialize(new { sessionId, role = "projector", sourceId = FakeVideoCapture.Display1 }))));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("recording.setPip", JsonSerializer.Serialize(new { sessionId, enabled = true, size = 3 }))));
        Assert.Equal("""{"videoTracks":[]}""", (await _host.ResultAsync("recording.setVideoSource", JsonSerializer.Serialize(new { sessionId, role = "camera", sourceId = (string?)null }))).GetRawText());
        Assert.Equal("{}", (await _host.ResultAsync("recording.setPip", JsonSerializer.Serialize(new { sessionId, enabled = false }))).GetRawText());
    }

    [Fact]
    public async Task OpenPrivacySettingsOpensTheRightWindowsPage()
    {
        await _host.ResultAsync("video.openPrivacySettings", """{"which":"camera"}""");
        await _host.ResultAsync("video.openPrivacySettings", """{"which":"screenCapture"}""");

        Assert.Equal(["ms-settings:privacy-webcam", "ms-settings:privacy-graphicscaptureprogrammatic"], _host.Launcher.Opened.Select(u => u.OriginalString));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("video.openPrivacySettings", """{"which":"microphone"}""")));
        _host.Launcher.Succeeds = false;
        Assert.Equal("app.openExternal.failed", Error(await _host.CallAsync("video.openPrivacySettings", """{"which":"camera"}""")));
    }

    [Fact]
    public async Task SettingsCarryTheVideoDefaultsAndMergeThemFieldByField()
    {
        var settings = await _host.ResultAsync("settings.get");
        Assert.Equal(
            """{"recordVideo":false,"defaultSource":"screen","resolution":"display","frameRate":30,"quality":"balanced","defaultDisplayId":null,"defaultCameraId":null,"pipCorner":"bottomRight","hardwareEncoder":true,"includePointer":true}""",
            settings.GetProperty("video").GetRawText());

        var changed = await _host.ResultAsync("settings.set", """{"video":{"recordVideo":true,"frameRate":60,"defaultCameraId":"camera:usb-test-camera"}}""");
        Assert.Equal((true, 60, "camera:usb-test-camera", "balanced"), (changed.GetProperty("video").GetProperty("recordVideo").GetBoolean(), changed.GetProperty("video").GetProperty("frameRate").GetInt32(), changed.GetProperty("video").GetProperty("defaultCameraId").GetString(), changed.GetProperty("video").GetProperty("quality").GetString()));

        // A page that replaces the recording block whole (1.x) does not reset the video defaults.
        await _host.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":30}}""");
        var cleared = await _host.ResultAsync("settings.set", """{"video":{"defaultCameraId":null}}""");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("video").GetProperty("defaultCameraId").ValueKind);
        Assert.Equal(60, cleared.GetProperty("video").GetProperty("frameRate").GetInt32());

        foreach (var bad in new[] { """{"video":{"frameRate":25}}""", """{"video":{"quality":"huge"}}""", """{"video":{"pipCorner":"middle"}}""", """{"video":{"resolution":"4k"}}""", """{"video":{"defaultDisplayId":3}}""", """{"storage":{"removeVideoOlderThanDays":0}}""" })
        {
            Assert.Equal("settings.invalidValue", Error(await _host.CallAsync("settings.set", bad)));
        }

        var storage = await _host.ResultAsync("settings.set", """{"storage":{"removeVideoOlderThanDays":90}}""");
        Assert.Equal("""{"reclaimOlderThanDays":null,"removeVideoOlderThanDays":90}""", storage.GetProperty("storage").GetRawText());
        Assert.Equal(60, _host.Settings.Current.Video.FrameRate);
    }

    [Fact]
    public void TheEventsHaveTheirShape()
    {
        var status = new VideoTrackStatus("screen", "screen", "recording", FakeVideoCapture.Display1, "Display 1", null, "NVIDIA H.264 Encoder MFT", true, 2560, 1440, 30, 12_000_000, 2_400_000, 900, 2, 9_000_000, 30_000, 120);
        Assert.Equal(
            """{"event":"recording.videoStatus","payload":{"sessionId":"s1","videoTracks":[{"trackId":"screen","role":"screen","state":"recording","sourceId":"display:\\\\.\\DISPLAY1","sourceName":"Display 1","reason":null,"encoder":"NVIDIA H.264 Encoder MFT","hardware":true,"width":2560,"height":1440,"frameRate":30,"targetBitsPerSecond":12000000,"actualBitsPerSecond":2400000,"framesWritten":900,"framesDropped":2,"bytesWritten":9000000,"durationMs":30000,"startOffsetMs":120}]}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.RecordingVideoStatus, new RecordingVideoStatusPayload("s1", [status]), VideoBridgeJsonContext.Default.BridgeEventEnvelopeRecordingVideoStatusPayload));
        Assert.Equal(
            """{"event":"recording.videoProblem","payload":{"sessionId":"s1","trackId":"screen","role":"screen","state":"waiting","code":"video.sourceLost","message":"m","atMs":1420000}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.RecordingVideoProblem, new RecordingVideoProblemPayload("s1", "screen", "screen", "waiting", "video.sourceLost", "m", 1_420_000), VideoBridgeJsonContext.Default.BridgeEventEnvelopeRecordingVideoProblemPayload));
        Assert.Equal(
            """{"event":"video.sourcesChanged","payload":{}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.VideoSourcesChanged, new EmptyPayload(), VideoBridgeJsonContext.Default.BridgeEventEnvelopeEmptyPayload));
    }

    [Fact]
    public void TheSummaryTrackInfoAndExportChoiceHaveTheirShape()
    {
        Assert.Equal(
            """{"state":"removed","layout":"screenAndCamera","width":2560,"height":1440,"frameRate":30,"durationMs":60000,"sizeBytes":0,"removedAt":"2026-10-06T10:00:00+01:00","bytesFreed":1900000000}""",
            JsonSerializer.Serialize(new VideoSummary("removed", "screenAndCamera", 2560, 1440, 30, 60_000, 0, new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1)), 1_900_000_000), VideoBridgeJsonContext.Default.VideoSummary));
        Assert.Equal(
            """{"id":"camera","role":"camera","sourceKind":"camera","name":"Cam","url":null,"width":1920,"height":1080,"frameRate":30,"encoder":"e","hardware":false,"startOffsetMs":5,"durationMs":6,"sizeBytes":7,"framesWritten":8,"framesDropped":0,"endedEarlyAtMs":null,"endReason":null,"sha256":null}""",
            JsonSerializer.Serialize(new VideoTrackInfo("camera", "camera", "camera", "Cam", null, 1920, 1080, 30, "e", false, 5, 6, 7, 8, 0, null, null, null), VideoBridgeJsonContext.Default.VideoTrackInfo));
        Assert.Equal("""{"on":false,"format":"mp4","layout":"oneFile"}""", JsonSerializer.Serialize(new ExportVideoChoice(), VideoBridgeJsonContext.Default.ExportVideoChoice));
    }

    [Fact]
    public void TheSourceWatchTellsThePageOnlyWhenSomethingCameOrWent()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var watch = new VideoSourceWatch(_capture, _host.Get<BridgeEventPublisher>(), time, NullLogger<VideoSourceWatch>.Instance);
        watch.Listed(_capture.ListSources());

        watch.Tick();
        Assert.Empty(_host.Sink.Payloads(BridgeEventNames.VideoSourcesChanged));

        _capture.MoreWindows.Add(new VideoSource("window:FF", "window", "New window", "App · 800 × 600", 800, 600, 0, false, false, false, "App"));
        watch.Tick();
        Assert.Single(_host.Sink.Payloads(BridgeEventNames.VideoSourcesChanged));

        // A minute after the last listing the watch stops looking.
        time.Advance(VideoSourceWatch.IdleAfter + TimeSpan.FromSeconds(1));
        _capture.MoreWindows.Clear();
        watch.Tick();
        Assert.Single(_host.Sink.Payloads(BridgeEventNames.VideoSourcesChanged));
    }
}
