using System.Text.Json;
using Memento.Audio;
using Memento.Video.Encoding;
using Memento.Video.Recording;
using Memento.Video.Sources;
using Memento.Video.Tests.Fakes;
using Memento.Core.Projects;
using Memento.Video.Tracks;

namespace Memento.Video.Tests.Recording;

public sealed class VideoCaptureSessionTests : IDisposable
{
    private const long Period = 333_333;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "memento-video-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeWriterFactory _writers = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(VideoCaptureSession Session, FakeVideoSource Source, long Origin)> Start(VideoSourceKind kind = VideoSourceKind.Display)
    {
        var id = kind switch
        {
            VideoSourceKind.Window => VideoSourceId.Window(new IntPtr(0x1234)),
            VideoSourceKind.Camera => VideoSourceId.Camera("camera-link"),
            _ => VideoSourceId.Display(@"\\.\DISPLAY1"),
        };
        var source = new FakeVideoSource(id, kind == VideoSourceKind.Window ? "Team call" : "Display 1");
        // Slot 0 a second from now, so the first tick of every test is slot 0 whatever the machine's speed.
        var origin = QpcClock.Now + QpcClock.TicksPerSecond;
        var options = new VideoSessionOptions(kind == VideoSourceKind.Camera ? "camera" : "screen", kind == VideoSourceKind.Camera ? "camera" : "screen", _folder, new VideoEncoderSettings(1920, 1080), origin);
        var session = new VideoCaptureSession(options, source, _writers);
        await session.StartAsync(startPacingThread: false, CancellationToken.None);
        return (session, source, origin);
    }

    private static void Ticks(VideoCaptureSession session, long origin, int from, int count)
    {
        for (var k = from; k < from + count; k++)
        {
            session.TickOnce(origin + (k * Period) + 10);
        }
    }

    private static async Task<VideoSessionEventArgs> Next(TaskCompletionSource<VideoSessionEventArgs> problem) =>
        await problem.Task.WaitAsync(TimeSpan.FromSeconds(5));

    private static TaskCompletionSource<VideoSessionEventArgs> Watch(VideoCaptureSession session)
    {
        var problem = new TaskCompletionSource<VideoSessionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Problem += (_, e) => problem.TrySetResult(e);
        return problem;
    }

    [Fact]
    public async Task FramesGoToTheFirstPartAndStopDescribesTheTrack()
    {
        var (session, source, origin) = await Start();
        Ticks(session, origin, 0, 30);

        var track = await session.StopAsync(CancellationToken.None);

        Assert.True(source.Stopped);
        Assert.True(_writers.Writers[0].Finalized);
        Assert.Equal(Path.Combine(_folder, "tracks/screen.mp4"), _writers.Writers[0].FilePath);
        Assert.Equal("screen", track.Id);
        Assert.Equal("video", track.Kind);
        Assert.Equal("display", track.SourceKind);
        Assert.Equal(@"display:\\.\DISPLAY1", track.SourceId);
        Assert.Equal("tracks/screen.mp4", track.File);
        Assert.Equal(30, track.FramesWritten);
        Assert.Equal(999, track.DurationMs);
        Assert.Equal(0, track.StartOffsetMs);
        Assert.True(track.HardwareEncoder);
        Assert.Single(track.Parts);
        Assert.Null(track.EndReason);
    }

    [Fact]
    public async Task PausingTheRecordingLeavesThePausedTimeOutOfTheVideo()
    {
        var (session, _, origin) = await Start();
        Ticks(session, origin, 0, 10);

        session.Pause();
        Assert.Equal(VideoSessionState.Paused, session.State);
        Ticks(session, origin, 10, 10);
        Assert.Equal(10, _writers.Writers[0].Timestamps.Count);

        session.Resume();
        var resumed = QpcClock.Now;
        for (var k = 0; k < 5; k++)
        {
            session.TickOnce(resumed + (k * Period) + 10);
        }

        Assert.Equal(Enumerable.Range(0, 15).Select(k => k * Period), _writers.Writers[0].Timestamps);
        Assert.Equal(VideoSessionState.Recording, session.State);
    }

    [Fact]
    public async Task AHardwareEncoderFailureContinuesInASoftwarePartAndSaysWhen()
    {
        var (session, _, origin) = await Start();
        var problem = Watch(session);
        Ticks(session, origin, 0, 10);
        var hardware = _writers.Writers[0];
        hardware.Failure = new VideoCaptureException(VideoErrorCode.EncoderFailed, VideoErrors.HardwareEncoderFailed());
        hardware.Next = VideoWriteResult.Failed;

        Ticks(session, origin, 10, 11);
        var e = await Next(problem);

        Assert.Equal(VideoErrorCode.EncoderFailed, e.Error.Code);
        Assert.Equal(VideoSessionState.Recording, e.State);
        Assert.StartsWith("Hardware encoder failed at ", e.Error.Message, StringComparison.Ordinal);
        Assert.Equal(2, _writers.Writers.Count);
        Assert.False(_writers.Writers[1].Encoder.IsHardware);
        Assert.Equal(Path.Combine(_folder, "tracks/screen.part2.mp4"), _writers.Writers[1].FilePath);
        Assert.True(hardware.Finalized);
        Assert.Equal(10, _writers.Writers[1].Timestamps.Count);

        var track = await session.StopAsync(CancellationToken.None);
        Assert.Equal(["tracks/screen.mp4", "tracks/screen.part2.mp4"], track.Parts.Select(p => p.File));
        Assert.Equal(11 * Period / 10_000, track.Parts[1].StartOffsetMs);
        Assert.Equal("Microsoft H.264 software encoder", track.Parts[1].Encoder);
        Assert.Null(track.EndReason);
        Assert.Equal(VideoSessionState.Stopped, session.State);
    }

    [Fact]
    public async Task AFullDiskEndsTheVideoTrackAndKeepsWhatWasWritten()
    {
        var (session, _, origin) = await Start();
        var problem = Watch(session);
        Ticks(session, origin, 0, 5);
        _writers.Writers[0].Failure = new VideoCaptureException(VideoErrorCode.DiskFull, VideoErrors.DiskFull("screen"));
        _writers.Writers[0].Next = VideoWriteResult.Failed;

        Ticks(session, origin, 5, 3);
        var e = await Next(problem);

        Assert.Equal(VideoSessionState.Ended, e.State);
        Assert.Equal(VideoErrorCode.DiskFull, e.Error.Code);
        Assert.Single(_writers.Writers);
        Assert.True(_writers.Writers[0].Finalized);
        var track = await session.StopAsync(CancellationToken.None);
        Assert.Equal("diskFull", track.EndReason);
        Assert.NotNull(track.EndedEarlyAtMs);
        Assert.Equal(5, track.FramesWritten);
    }

    [Fact]
    public async Task AClosedWindowEndsTheTrackWithTheWindowsNameAndTheTime()
    {
        var (session, source, origin) = await Start(VideoSourceKind.Window);
        var problem = Watch(session);
        Ticks(session, origin, 0, 5);

        source.Lose(VideoErrorCode.SourceLost, VideoErrors.WindowClosed(source.Name));
        var e = await Next(problem);
        Ticks(session, origin, 5, 5);

        Assert.Equal(VideoSessionState.Ended, e.State);
        Assert.StartsWith("\u201cTeam call\u201d was closed at 0:", e.Error.Message, StringComparison.Ordinal);
        Assert.Contains("everything before it is saved", e.Error.Message, StringComparison.Ordinal);
        Assert.Equal(5, _writers.Writers[0].Timestamps.Count);
        var track = await session.StopAsync(CancellationToken.None);
        Assert.Equal("sourceLost", track.EndReason);
        Assert.True(_writers.Writers[0].Finalized);
    }

    [Fact]
    public async Task ADisconnectedDisplayWaitsAndAnotherDisplayContinuesTheSameTrack()
    {
        var (session, source, origin) = await Start();
        var problem = Watch(session);
        Ticks(session, origin, 0, 5);

        source.Lose(VideoErrorCode.SourceLost, VideoErrors.DisplayDisconnected(source.Name));
        var e = await Next(problem);
        Assert.Equal(VideoSessionState.Waiting, e.State);
        Assert.StartsWith("Display 1 disconnected at 0:", e.Error.Message, StringComparison.Ordinal);
        Ticks(session, origin, 5, 5);
        Assert.Equal(5, _writers.Writers[0].Timestamps.Count);

        var other = new FakeVideoSource(VideoSourceId.Display(@"\\.\DISPLAY2"), "Display 2");
        await session.SwitchSourceAsync(other, CancellationToken.None);
        Ticks(session, origin, 10, 5);

        Assert.Equal(VideoSessionState.Recording, session.State);
        Assert.Equal(10, _writers.Writers[0].Timestamps.Count);
        Assert.Single(_writers.Writers);
        Assert.Equal(10 * Period, _writers.Writers[0].Timestamps[5]);
        Assert.True(source.Stopped);
    }

    [Fact]
    public async Task AMinimisedWindowHoldsItsLastPictureAndResumes()
    {
        var (session, source, origin) = await Start(VideoSourceKind.Window);
        Ticks(session, origin, 0, 3);

        source.SetPaused(true);
        Assert.Equal(VideoSessionState.Waiting, session.Status().State);
        Assert.Equal("Window minimised", session.Status().Reason);
        Ticks(session, origin, 3, 4);
        source.SetPaused(false);
        Ticks(session, origin, 7, 2);

        // The gap stays in the timeline (the frame before it lasts longer); nothing counts as dropped.
        Assert.Equal([0, Period, 2 * Period, 7 * Period, 8 * Period], _writers.Writers[0].Timestamps);
        Assert.Equal(0, session.Status().FramesDropped);
    }

    [Fact]
    public async Task NoEncoderAtAllFailsTheStartWithTheSpecificError()
    {
        _writers.NoEncoderAtAll = true;
        var source = new FakeVideoSource(VideoSourceId.Display(@"\\.\DISPLAY1"), "Display 1");
        var session = new VideoCaptureSession(new VideoSessionOptions("screen", "screen", _folder, new VideoEncoderSettings(1280, 720), QpcClock.Now), source, _writers);

        var ex = await Assert.ThrowsAsync<VideoCaptureException>(() => session.StartAsync(startPacingThread: false, CancellationToken.None));

        Assert.Equal(VideoErrorCode.EncoderUnavailable, ex.Code);
        Assert.Contains("Audio recording is not affected", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACheckpointFlushesTheFileAndDescribesEveryPart()
    {
        var (session, _, origin) = await Start();
        Ticks(session, origin, 0, 4);

        var checkpoint = session.Checkpoint();
        var json = JsonSerializer.Serialize(checkpoint, VideoJsonContext.Default.VideoTrackCheckpoint);
        var back = JsonSerializer.Deserialize(json, VideoJsonContext.Default.VideoTrackCheckpoint)!;

        Assert.Equal(1, _writers.Writers[0].Checkpoints);
        Assert.Contains("\"trackId\": \"screen\"", json, StringComparison.Ordinal);
        Assert.Equal("tracks/screen.mp4", back.Parts[0].File);
        Assert.Equal(4, back.FramesWritten);
        await session.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StatusReportsEncoderSizeAndCounts()
    {
        var (session, _, origin) = await Start();
        Ticks(session, origin, 0, 30);
        _writers.Writers[0].Next = VideoWriteResult.NoFreeSurface;
        Ticks(session, origin, 30, 1);

        var status = session.Status();

        Assert.Equal("Test hardware encoder", status.EncoderName);
        Assert.True(status.HardwareEncoder);
        Assert.Equal(1920, status.Width);
        Assert.Equal(30, status.FrameRate);
        Assert.Equal(30, status.FramesWritten);
        Assert.Equal(1, status.FramesDropped);
        Assert.Equal(VideoBitrate.For(1920, 1080, 30, VideoQuality.Balanced), status.TargetBitsPerSecond);
        await session.StopAsync(CancellationToken.None);
    }
}
