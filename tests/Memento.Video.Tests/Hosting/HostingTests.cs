using Memento.Core.Bridge;
using Memento.Core.Settings;
using Memento.Video.Hosting;
using Memento.Video.Recording;

namespace Memento.Video.Tests.Hosting;

/// <summary>How Memento.Video's results reach the bridge: error codes, states and track sizes.</summary>
public sealed class HostingTests
{
    [Theory]
    [InlineData(VideoErrorCode.SourceLost, DomainErrorCodes.VideoSourceLost)]
    [InlineData(VideoErrorCode.SourceNotFound, DomainErrorCodes.VideoSourceNotFound)]
    [InlineData(VideoErrorCode.CaptureUnsupported, DomainErrorCodes.VideoUnavailable)]
    [InlineData(VideoErrorCode.ScreenCapturePrivacyDenied, DomainErrorCodes.VideoCaptureBlocked)]
    [InlineData(VideoErrorCode.ProtectedContent, DomainErrorCodes.VideoProtectedContent)]
    [InlineData(VideoErrorCode.CameraPrivacyDenied, DomainErrorCodes.VideoCameraPrivacy)]
    [InlineData(VideoErrorCode.CameraInUse, DomainErrorCodes.VideoCameraInUse)]
    [InlineData(VideoErrorCode.EncoderUnavailable, DomainErrorCodes.VideoEncoderUnavailable)]
    [InlineData(VideoErrorCode.EncoderFailed, DomainErrorCodes.VideoEncoderFailed)]
    [InlineData(VideoErrorCode.DiskFull, DomainErrorCodes.VideoDiskFull)]
    [InlineData(VideoErrorCode.WriteFailed, DomainErrorCodes.VideoWriteFailed)]
    [InlineData(VideoErrorCode.NoGraphicsDevice, DomainErrorCodes.VideoUnavailable)]
    public void EveryLibraryErrorHasABridgeCode(VideoErrorCode code, string expected) => Assert.Equal(expected, VideoCodes.Of(code));

    [Fact]
    public void EveryLibraryErrorIsMapped() =>
        Assert.All(Enum.GetValues<VideoErrorCode>(), code => Assert.StartsWith("video.", VideoCodes.Of(code), StringComparison.Ordinal));

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
