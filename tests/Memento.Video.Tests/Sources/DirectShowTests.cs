using System.Buffers.Binary;
using Memento.Audio;
using Memento.Video.Export;
using Memento.Video.Sources;
using Memento.Video.Tests.Fakes;

namespace Memento.Video.Tests.Sources;

/// <summary>
/// DirectShow cameras without DirectShow (ENGINE-NOTES.md §R): reading media types, converting every layout to NV12,
/// the bounded hand-over, the virtual camera watch, the errors, and the source itself on a scripted feed.
/// </summary>
public sealed class DirectShowTests
{
    private const string ObsKey = "dshow:clsid:{A3FCE0F5-3493-419F-958A-ABA1250EC20B}";
    private const string WebcamMf = @"\\?\usb#vid_046d&pid_085e&mi_00#7&2b4c&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global";
    private const string WebcamDshow = @"\\?\USB#VID_046D&PID_085E&MI_00#7&2B4C&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global";

    [Fact]
    public void SubtypesAreKnownByTheirFourCc()
    {
        Assert.Equal(new Guid("3231564E-0000-0010-8000-00AA00389B71"), DirectShowFormats.Nv12);
        Assert.Equal(new Guid("32595559-0000-0010-8000-00AA00389B71"), DirectShowFormats.Yuy2);
        Assert.Equal(new Guid("47504A4D-0000-0010-8000-00AA00389B71"), DirectShowFormats.Mjpg);
        Assert.Equal(RawPixelFormat.I420, DirectShowFormats.PixelOf(DirectShowFormats.Iyuv));
        Assert.Equal(RawPixelFormat.Rgb32, DirectShowFormats.PixelOf(DirectShowFormats.Argb32));
        Assert.Equal(RawPixelFormat.Unsupported, DirectShowFormats.PixelOf(DirectShowFormats.Mjpg));
        Assert.True(DirectShowFormats.IsCompressed(DirectShowFormats.Mjpg));
        Assert.Equal("MJPG", DirectShowFormats.NameOf(DirectShowFormats.Mjpg));
        Assert.Equal("RGB24", DirectShowFormats.NameOf(DirectShowFormats.Rgb24));
        Assert.Equal("{00000000-0000-0000-0000-000000000001}", DirectShowFormats.NameOf(new Guid("00000000-0000-0000-0000-000000000001")));
        Assert.Equal([DirectShowFormats.Nv12, DirectShowFormats.Yuy2, DirectShowFormats.Rgb32, DirectShowFormats.Rgb24], DirectShowFormats.Fallbacks);
    }

    [Fact]
    public void AVideoInfoHeaderGivesSizeStrideAndFrameRate()
    {
        var nv12 = DirectShowFormats.Parse(DirectShowFormats.Nv12, FormatVideoInfo, VideoInfo(1280, 720, 12, 333_333));
        Assert.Equal(new RawVideoFormat(RawPixelFormat.Nv12, 1280, 720, 1280, false, 30, "NV12"), nv12);
        Assert.Equal(1280 * 720 * 3 / 2, nv12!.MinimumLength);

        var rgb = DirectShowFormats.Parse(DirectShowFormats.Rgb24, FormatVideoInfo, VideoInfo(641, 480, 24, 400_000));
        Assert.Equal((1924, true, 25.0), (rgb!.Stride, rgb.BottomUp, rgb.FrameRate));
        Assert.Equal((640, 480), rgb.EvenSize);

        var topDown = DirectShowFormats.Parse(DirectShowFormats.Rgb32, FormatVideoInfo, VideoInfo(320, -240, 32, 0));
        Assert.Equal((240, false, 0.0, 1280), (topDown!.Height, topDown.BottomUp, topDown.FrameRate, topDown.Stride));

        var yuy2 = DirectShowFormats.Parse(DirectShowFormats.Yuy2, FormatVideoInfo2, VideoInfo(1920, 1080, 16, 166_666, version2: true));
        Assert.Equal((RawPixelFormat.Yuy2, 3840, 60.0), (yuy2!.Pixel, yuy2.Stride, yuy2.FrameRate));

        var mjpg = DirectShowFormats.Parse(DirectShowFormats.Mjpg, FormatVideoInfo, VideoInfo(1920, 1080, 24, 333_333));
        Assert.Equal((RawPixelFormat.Unsupported, "MJPG"), (mjpg!.Pixel, mjpg.SubtypeName));
    }

    [Fact]
    public void AFormatBlockThatIsNotAVideoInfoHeaderIsNotRead()
    {
        Assert.Null(DirectShowFormats.Parse(DirectShowFormats.Nv12, Guid.NewGuid(), VideoInfo(1280, 720, 12, 333_333)));
        Assert.Null(DirectShowFormats.Parse(DirectShowFormats.Nv12, FormatVideoInfo, new byte[60]));
        Assert.Null(DirectShowFormats.Parse(DirectShowFormats.Nv12, FormatVideoInfo, VideoInfo(0, 720, 12, 333_333)));
    }

    [Fact]
    public void Nv12WithAPaddedStrideIsCopiedRowByRow()
    {
        var format = new RawVideoFormat(RawPixelFormat.Nv12, 4, 2, 8, false, 30, "NV12");
        byte[] source = [10, 11, 12, 13, 0, 0, 0, 0, 20, 21, 22, 23, 0, 0, 0, 0, 100, 200, 101, 201, 0, 0, 0, 0];
        var target = new Nv12Frame(4, 2);

        Assert.True(RawFrameConverter.ToNv12(format, source, target));
        Assert.Equal([10, 11, 12, 13, 20, 21, 22, 23, 100, 200, 101, 201], target.Data);
    }

    [Theory]
    [InlineData(RawPixelFormat.I420, 100, 200)]
    [InlineData(RawPixelFormat.Yv12, 200, 100)]
    public void PlanarChromaIsInterleaved(RawPixelFormat pixel, byte u, byte v)
    {
        var format = new RawVideoFormat(pixel, 4, 2, 4, false, 30, pixel.ToString());
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8, 100, 101, 200, 201];
        var target = new Nv12Frame(4, 2);

        Assert.True(RawFrameConverter.ToNv12(format, source, target));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, u, v, (byte)(u + 1), (byte)(v + 1) }, target.Data);
    }

    [Fact]
    public void Yuy2AndUyvyKeepLumaAndAverageChromaOverTwoRows()
    {
        // Two rows of two pixels: Y0 U Y1 V.
        byte[] yuy2 = [16, 100, 17, 200, 18, 110, 19, 210];
        byte[] uyvy = [100, 16, 200, 17, 110, 18, 210, 19];
        foreach (var (pixel, bytes) in new[] { (RawPixelFormat.Yuy2, yuy2), (RawPixelFormat.Uyvy, uyvy) })
        {
            var target = new Nv12Frame(2, 2);
            Assert.True(RawFrameConverter.ToNv12(new RawVideoFormat(pixel, 2, 2, 4, false, 30, "x"), bytes, target));
            Assert.Equal([16, 17, 18, 19, 105, 205], target.Data);
        }
    }

    [Fact]
    public void RgbBecomesBt709LimitedRangeAndBottomUpRowsAreFlipped()
    {
        // RGB32, bottom-up: the stored first row is the picture's bottom row (white), the second its top row (red).
        byte[] source = [255, 255, 255, 0, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0];
        var target = new Nv12Frame(2, 2);

        Assert.True(RawFrameConverter.ToNv12(new RawVideoFormat(RawPixelFormat.Rgb32, 2, 2, 8, true, 30, "RGB32"), source, target));
        Assert.Equal(63, target.Data[0]);
        Assert.Equal(235, target.Data[2]);
        Assert.InRange(target.Data[4], 112, 118);
        Assert.InRange(target.Data[5], 180, 188);

        var black = new Nv12Frame(2, 2);
        Assert.True(RawFrameConverter.ToNv12(new RawVideoFormat(RawPixelFormat.Rgb24, 2, 2, 8, false, 30, "RGB24"), new byte[16], black));
        Assert.Equal([16, 16, 16, 16, 128, 128], black.Data);
    }

    [Fact]
    public void ShortBuffersAndUnsupportedLayoutsAreRefused()
    {
        var target = new Nv12Frame(4, 2);

        Assert.False(RawFrameConverter.ToNv12(new RawVideoFormat(RawPixelFormat.Nv12, 4, 2, 4, false, 30, "NV12"), new byte[11], target));
        Assert.False(RawFrameConverter.ToNv12(new RawVideoFormat(RawPixelFormat.Unsupported, 4, 2, 4, false, 30, "MJPG"), new byte[64], target));
        Assert.False(RawFrameConverter.ToNv12(new RawVideoFormat(RawPixelFormat.Nv12, 6, 2, 6, false, 30, "NV12"), new byte[18], target));
    }

    [Fact]
    public void TheHandOverDropsAndCountsWhenEveryBufferIsTakenAndNeverWaits()
    {
        using var queue = new RawFrameQueue(slots: 3);
        var picture = new byte[16];

        Assert.True(queue.TryWrite(picture, 1));
        Assert.True(queue.TryWrite(picture, 2));
        Assert.True(queue.TryWrite(picture, 3));
        Assert.False(queue.TryWrite(picture, 4));
        Assert.Equal((3L, 1L), (queue.Written, queue.Dropped));

        var first = queue.Take(TimeSpan.Zero);
        Assert.Equal(1, first!.Qpc);
        queue.Return(first);
        Assert.True(queue.TryWrite([1, 2, 3], 5));
        Assert.Equal(2, queue.Take(TimeSpan.Zero)!.Qpc);
        Assert.Equal(3, queue.Take(TimeSpan.Zero)!.Qpc);
        var last = queue.Take(TimeSpan.Zero)!;
        Assert.Equal((5L, 3), (last.Qpc, last.Length));
        Assert.Null(queue.Take(TimeSpan.Zero));

        queue.Complete();
        Assert.False(queue.TryWrite(picture, 6));
        Assert.Null(queue.Take(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TheObsWatchSaysSendingOnlyWhileTheQueueIsReadyAndMoving()
    {
        (uint WriteIndex, uint State)? header = null;
        long now = 0;
        var watch = new VirtualCameraWatch("OBS", () => header, () => now);

        Assert.False(watch.IsSending());
        header = (5, 1);
        Assert.False(watch.IsSending());
        header = (5, VirtualCameraWatch.StateReady);
        Assert.True(watch.IsSending());
        now = 1500;
        Assert.True(watch.IsSending());
        header = (6, VirtualCameraWatch.StateReady);
        now = 3000;
        Assert.True(watch.IsSending());
        now = 5100;
        Assert.False(watch.IsSending());
        header = (7, 3);
        Assert.False(watch.IsSending());
    }

    [Fact]
    public void OnlyTheObsVirtualCameraIsWatched()
    {
        Assert.Equal("OBS", VirtualCameraWatch.For(VideoSourceId.Camera(ObsKey))!.App);
        Assert.NotNull(VirtualCameraWatch.For(VideoSourceId.Camera(ObsKey.ToLowerInvariant())));
        Assert.Null(VirtualCameraWatch.For(VideoSourceId.Camera("dshow:" + WebcamDshow)));
        Assert.Null(VirtualCameraWatch.For(VideoSourceId.Camera(WebcamMf)));
    }

    [Theory]
    [InlineData(unchecked((int)0x80040217))]
    [InlineData(unchecked((int)0x80040207))]
    [InlineData(unchecked((int)0x8004022A))]
    public void AFormatNoFilterReadsIsSaidSo(int hresult)
    {
        var error = CameraErrors.Classify(hresult, "Capture card", CameraAccess.Unknown, null);

        Assert.Equal(VideoErrorCode.SourceNotFound, error.Code);
        Assert.StartsWith("Capture card sends its picture in a format Memento can't read.", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("Choose another format in the camera's own software, or pick another camera.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASharingViolationIsACameraInUse() =>
        Assert.Equal(VideoErrorCode.CameraInUse, CameraErrors.Classify(CameraErrors.SharingViolation, "Brio", CameraAccess.Allowed, "Teams").Code);

    [Fact]
    public void TheVirtualCameraSentencesNameTheAppAndTheFix()
    {
        Assert.Equal(
            "OBS Virtual Camera is not started in OBS, so it has no picture to record. Recording continues with the screen and all audio tracks. Start the virtual camera in OBS, then retry the camera.",
            VideoErrors.VirtualCameraNotStarted("OBS Virtual Camera", "OBS"));
        Assert.Equal(
            "OBS Virtual Camera stopped at 1:05: OBS is no longer sending it a picture. The camera track ends there and everything before it is saved; the screen and audio keep recording. Start the virtual camera in OBS again, then turn the camera back on.",
            VideoErrors.VirtualCameraStopped("OBS Virtual Camera", "OBS", 65_000));
        Assert.Contains("(MJPG)", VideoErrors.CameraFormatRefused("Cam", "MJPG"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSourceConvertsEachPictureAndKeepsTheLatest()
    {
        var feed = new FakeFrameFeed(new RawVideoFormat(RawPixelFormat.Yuy2, 4, 2, 8, false, 30, "YUY2"));
        await using var source = new DirectShowCameraSource(null, VideoSourceId.Camera(ObsKey), "Capture card", () => feed, watch: null);
        Assert.False(source.TryGetLatest(out _));

        await source.StartAsync(CancellationToken.None);
        feed.Push([16, 128, 20, 128, 24, 128, 28, 128, 32, 128, 36, 128, 40, 128, 44, 128]);
        feed.Push([50, 90, 50, 170, 50, 90, 50, 170, 50, 90, 50, 170, 50, 90, 50, 170]);
        await WaitFor(() => source.Statistics.FramesArrived == 2);

        Assert.True(source.TryGetLatest(out var frame));
        Assert.Equal((4, 2, 2L, (Vortice.Direct3D11.ID3D11Texture2D?)null), (frame.Width, frame.Height, frame.Sequence, frame.Texture));
        Assert.InRange(QpcClock.Now - frame.QpcTicks, 0, 5 * QpcClock.TicksPerSecond);
        Assert.Equal([50, 50, 50, 50, 50, 50, 50, 50, 90, 170, 90, 170], ((ICameraSnapshot)source).Snapshot()!.Data);
        Assert.Equal(VideoSourceState.Running, source.State);
        Assert.Equal((0L, 0L), (source.FramesDropped, source.FramesUnreadable));

        feed.Push(new byte[3]);
        await WaitFor(() => source.FramesUnreadable == 1);
        await source.StopAsync();
        Assert.True(feed.Stopped);
        Assert.Equal(VideoSourceState.Stopped, source.State);
    }

    [Fact]
    public async Task ADeviceThatGoesAwayIsLostAsDisconnected()
    {
        var feed = new FakeFrameFeed(new RawVideoFormat(RawPixelFormat.Nv12, 2, 2, 2, false, 30, "NV12"));
        await using var source = new DirectShowCameraSource(null, VideoSourceId.Camera("dshow:name:Old capture"), "Old capture", () => feed, watch: null);
        var lost = Lost(source);
        await source.StartAsync(CancellationToken.None);

        feed.Fail(CameraErrors.DeviceRemoved);
        var e = await lost.Task.WaitAsync(Patience.Ceiling);

        Assert.Equal(VideoErrorCode.SourceLost, e.Error.Code);
        Assert.Equal(VideoSourceState.Lost, source.State);
        Assert.Same(e.Error, source.Loss);
    }

    [Fact]
    public async Task ACameraThatSendsNothingForAWhileIsLost()
    {
        var feed = new FakeFrameFeed(new RawVideoFormat(RawPixelFormat.Nv12, 2, 2, 2, false, 30, "NV12"));
        await using var source = new DirectShowCameraSource(null, VideoSourceId.Camera("dshow:name:Silent"), "Silent", () => feed, watch: null, stallAfter: TimeSpan.FromMilliseconds(700));
        var lost = Lost(source);
        await source.StartAsync(CancellationToken.None);

        var e = await lost.Task.WaitAsync(Patience.Ceiling);

        Assert.Equal(VideoErrorCode.SourceLost, e.Error.Code);
        Assert.Contains("Silent was disconnected", e.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVirtualCameraWhoseAppIsNotSendingIsRefusedAndOneThatStopsIsLost()
    {
        (uint WriteIndex, uint State)? header = null;
        long now = 0;
        var watch = new VirtualCameraWatch("OBS", () => header, () => now);
        var feed = new FakeFrameFeed(new RawVideoFormat(RawPixelFormat.Nv12, 2, 2, 2, false, 30, "NV12"));
        await using var refused = new DirectShowCameraSource(null, VideoSourceId.Camera(ObsKey), "OBS Virtual Camera", () => feed, watch);

        var error = await Assert.ThrowsAsync<VideoCaptureException>(() => refused.StartAsync(CancellationToken.None));
        Assert.Equal((VideoErrorCode.SourceNotFound, "OBS"), (error.Code, error.Detail));
        Assert.StartsWith("OBS Virtual Camera is not started in OBS", error.Message, StringComparison.Ordinal);
        Assert.False(feed.Started);

        header = (1, VirtualCameraWatch.StateReady);
        await using var source = new DirectShowCameraSource(null, VideoSourceId.Camera(ObsKey), "OBS Virtual Camera", () => feed, watch);
        var lost = Lost(source);
        await source.StartAsync(CancellationToken.None);
        header = (2, 3);
        var e = await lost.Task.WaitAsync(Patience.Ceiling);

        Assert.Equal((VideoErrorCode.SourceLost, "OBS"), (e.Error.Code, e.Error.Detail));
        Assert.StartsWith("OBS Virtual Camera stopped: OBS is no longer sending it a picture.", e.Error.Message, StringComparison.Ordinal);
        Assert.StartsWith("OBS Virtual Camera stopped at 0:42:", e.Error.AtPosition!(42_000), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeedThatCannotStartLeavesNothingRunning()
    {
        var feed = new FakeFrameFeed(new RawVideoFormat(RawPixelFormat.Nv12, 2, 2, 2, false, 30, "NV12"))
        {
            FailOnStart = CameraErrors.Classify(CameraErrors.Busy, "Capture card", CameraAccess.Unknown, "Zoom"),
        };
        await using var source = new DirectShowCameraSource(null, VideoSourceId.Camera("dshow:name:Capture card"), "Capture card", () => feed, watch: null);

        var error = await Assert.ThrowsAsync<VideoCaptureException>(() => source.StartAsync(CancellationToken.None));

        Assert.Equal(VideoErrorCode.CameraInUse, error.Code);
        Assert.True(feed.Disposed);
        Assert.Equal(VideoSourceState.Stopped, source.State);
    }

    private static TaskCompletionSource<VideoSourceLostEventArgs> Lost(IVideoSource source)
    {
        var lost = new TaskCompletionSource<VideoSourceLostEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Lost += (_, e) => lost.TrySetResult(e);
        return lost;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static readonly Guid FormatVideoInfo = new("05589f80-c356-11ce-bf01-00aa0055595a");
    private static readonly Guid FormatVideoInfo2 = new("F72A76A0-EB0A-11d0-ACE4-0000C0CC16BA");

    /// <summary>A <c>VIDEOINFOHEADER</c> (or <c>VIDEOINFOHEADER2</c>) as a filter would hand it over.</summary>
    private static byte[] VideoInfo(int width, int height, short bitCount, long averageTimePerFrame, bool version2 = false)
    {
        var offset = version2 ? 72 : 48;
        var block = new byte[offset + 40];
        BinaryPrimitives.WriteInt64LittleEndian(block.AsSpan(40), averageTimePerFrame);
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(offset), 40);
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(offset + 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(offset + 8), height);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(offset + 12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(offset + 14), bitCount);
        return block;
    }
}
