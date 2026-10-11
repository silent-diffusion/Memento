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
}
