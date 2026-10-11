using Memento.Core.Bridge;
using Memento.Core.Settings;
using Memento.Video.Hosting;
using Memento.Video.Recording;

namespace Memento.Video.Tests.Hosting;

/// <summary>How Memento.Video's results reach the bridge: error codes, states and track sizes.</summary>
public sealed class HostingTests
{
    [Theory]
    [InlineData(VideoErrorCode.SourceLost, DomainErrorCodes.VideoSourceNotFound)]
    [InlineData(VideoErrorCode.SourceNotFound, DomainErrorCodes.VideoSourceNotFound)]
    [InlineData(VideoErrorCode.CaptureUnsupported, DomainErrorCodes.VideoCaptureBlocked)]
    [InlineData(VideoErrorCode.ScreenCapturePrivacyDenied, DomainErrorCodes.VideoCaptureBlocked)]
    [InlineData(VideoErrorCode.ProtectedContent, DomainErrorCodes.VideoProtectedContent)]
    [InlineData(VideoErrorCode.CameraPrivacyDenied, DomainErrorCodes.VideoCameraPrivacy)]
    [InlineData(VideoErrorCode.CameraInUse, DomainErrorCodes.VideoCameraInUse)]
    [InlineData(VideoErrorCode.EncoderUnavailable, DomainErrorCodes.VideoEncoderUnavailable)]
    [InlineData(VideoErrorCode.EncoderFailed, DomainErrorCodes.VideoEncoderUnavailable)]
    [InlineData(VideoErrorCode.DiskFull, DomainErrorCodes.VideoDiskFull)]
    [InlineData(VideoErrorCode.WriteFailed, DomainErrorCodes.VideoSourceNotFound)]
    [InlineData(VideoErrorCode.NoGraphicsDevice, DomainErrorCodes.VideoEncoderUnavailable)]
    public void EveryStartFailureHasOneOfTheSevenErrorCodes(VideoErrorCode code, string expected) => Assert.Equal(expected, VideoCodes.Of(code));

    [Fact]
    public void EveryLibraryErrorIsAnErrorCodeAndEveryProblemIsInTheClosedList()
    {
        string[] errors = [DomainErrorCodes.VideoSourceNotFound, DomainErrorCodes.VideoProtectedContent, DomainErrorCodes.VideoCaptureBlocked, DomainErrorCodes.VideoCameraPrivacy, DomainErrorCodes.VideoCameraInUse, DomainErrorCodes.VideoEncoderUnavailable, DomainErrorCodes.VideoDiskFull];
        Assert.All(Enum.GetValues<VideoErrorCode>(), code => Assert.Contains(VideoCodes.Of(code), errors));
        Assert.All(Enum.GetValues<VideoErrorCode>(), code => Assert.All(Enum.GetValues<Memento.Video.Sources.VideoSourceKind>(), kind => Assert.Contains(VideoCodes.Problem(code, kind), Memento.Core.Video.VideoProblemCodes.All)));
    }

    [Theory]
    [InlineData(VideoErrorCode.SourceLost, Memento.Video.Sources.VideoSourceKind.Display, "video.displayDisconnected")]
    [InlineData(VideoErrorCode.SourceLost, Memento.Video.Sources.VideoSourceKind.Window, "video.windowClosed")]
    [InlineData(VideoErrorCode.SourceLost, Memento.Video.Sources.VideoSourceKind.Camera, "video.sourceNotFound")]
    [InlineData(VideoErrorCode.SourceMinimised, Memento.Video.Sources.VideoSourceKind.Window, "video.windowMinimised")]
    [InlineData(VideoErrorCode.EncoderFailed, Memento.Video.Sources.VideoSourceKind.Display, "video.encoderFallback")]
    [InlineData(VideoErrorCode.CameraInUse, Memento.Video.Sources.VideoSourceKind.Camera, "video.cameraInUse")]
    public void ProblemsWhileRecordingHaveTheirCode(VideoErrorCode code, Memento.Video.Sources.VideoSourceKind kind, string expected) =>
        Assert.Equal(expected, VideoCodes.Problem(code, kind));

    [Theory]
    [InlineData(VideoSessionState.Created, "recording")]
    [InlineData(VideoSessionState.Recording, "recording")]
    [InlineData(VideoSessionState.Paused, "paused")]
    [InlineData(VideoSessionState.Waiting, "waiting")]
    [InlineData(VideoSessionState.Ended, "ended")]
    [InlineData(VideoSessionState.Stopped, "stopped")]
    public void SessionStatesHaveTheirBridgeNames(VideoSessionState state, string expected) => Assert.Equal(expected, VideoCodes.Of(state));

    [Theory]
    [InlineData(2560, 1440, VideoSettings.ResolutionDisplay, 2560, 1440)]
    [InlineData(2560, 1440, VideoSettings.Resolution1080, 1920, 1080)]
    [InlineData(2560, 1440, VideoSettings.Resolution720, 1280, 720)]
    [InlineData(1280, 720, VideoSettings.Resolution1080, 1280, 720)]
    [InlineData(1080, 1920, VideoSettings.Resolution1080, 1080, 1920)]
    [InlineData(1080, 1920, VideoSettings.Resolution720, 720, 1280)]
    [InlineData(901, 333, VideoSettings.ResolutionDisplay, 900, 332)]
    [InlineData(5120, 1440, VideoSettings.ResolutionDisplay, 4096, 1152)]
    public void AScreenTrackKeepsItsShapeWithinTheChosenResolution(int width, int height, string resolution, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), TrackSize.ForScreen(width, height, resolution));

    [Fact]
    public void CamerasAndQualitiesFollowSettings()
    {
        Assert.Equal((1920, 1080), TrackSize.ForCamera(VideoSettings.ResolutionDisplay));
        Assert.Equal((1280, 720), TrackSize.ForCamera(VideoSettings.Resolution720));
        Assert.Equal(Memento.Video.Encoding.VideoQuality.Best, TrackSize.Quality(VideoSettings.QualityBest));
        Assert.Equal(Memento.Video.Encoding.VideoQuality.Balanced, TrackSize.Quality("anything else"));
    }

    [Fact]
    public void MementosOwnWindowsAndProtectedOnesAreRefusedByTheHostNotOnlyThePicker()
    {
        // Audit 2, SA2-05: listed as excluded, but recording.setVideoSource and video.thumbnail accepted them.
        var own = new Memento.Video.Sources.VideoSourceInfo(Memento.Video.Sources.VideoSourceId.Window(0x1234), "Memento", "Memento", 800, 600, 0, false, IsExcluded: true, "Memento");
        var shielded = own with { IsExcluded = false, IsProtected = true, Name = "Player" };
        var ordinary = own with { IsExcluded = false, Name = "Notes" };

        Assert.Equal(DomainErrorCodes.VideoSourceNotFound, MediaFoundationVideoCapture.Refusal(own)!.Code);
        Assert.Contains("Memento does not record its own windows", MediaFoundationVideoCapture.Refusal(own)!.Message, StringComparison.Ordinal);
        Assert.Equal(DomainErrorCodes.VideoProtectedContent, MediaFoundationVideoCapture.Refusal(shielded)!.Code);
        Assert.Null(MediaFoundationVideoCapture.Refusal(ordinary));
    }

    [Fact]
    public void AThinTallWindowGivesAThumbnailAtMostTwiceAsTallAsWide()
    {
        // Audit 2, SA2-04: a 2 × 4096 window asked for a 640 × 1,310,720 bitmap (3.4 GB).
        using var thin = new System.Drawing.Bitmap(2, Thumbnails.MaxWindowSide);

        var thumbnail = Thumbnails.Encode(thin, 640);

        Assert.Equal((640, 1280), (thumbnail.Width, thumbnail.Height));
        Assert.StartsWith("data:image/jpeg;base64,", thumbnail.DataUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void CameraThumbnailsQueueApartFromDisplayAndWindowThumbnails()
    {
        var screens = MediaFoundationVideoCapture.GateFor(Memento.Video.Sources.VideoSourceKind.Display);

        Assert.Same(screens, MediaFoundationVideoCapture.GateFor(Memento.Video.Sources.VideoSourceKind.Window));
        Assert.NotSame(screens, MediaFoundationVideoCapture.GateFor(Memento.Video.Sources.VideoSourceKind.Camera));
    }

    [Trait("Category", "Hardware")]
    [Memento.Video.Tests.Hardware.VideoHardwareFact]
    public async Task ADisplayStillDoesNotWaitForACameraThatIsSlowToAnswer()
    {
        var capture = new MediaFoundationVideoCapture();
        var display = capture.ListSources().Displays[0];
        var cameras = MediaFoundationVideoCapture.GateFor(Memento.Video.Sources.VideoSourceKind.Camera);

        // A camera thumbnail waiting for its first picture holds the camera queue.
        await cameras.WaitAsync();
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var still = await capture.ThumbnailAsync(display.Id, 320, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.StartsWith("data:image/jpeg;base64,", still.DataUrl, StringComparison.Ordinal);
            Assert.True(clock.ElapsedMilliseconds < 1000, $"the display's still took {clock.ElapsedMilliseconds} ms");
        }
        finally
        {
            cameras.Release();
        }
    }
}
