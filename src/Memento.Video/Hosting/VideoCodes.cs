using Memento.Core.Bridge;
using Memento.Core.Video;
using Memento.Video.Recording;
using Memento.Video.Sources;

namespace Memento.Video.Hosting;

/// <summary>The bridge's names for the library's error codes and session states (BRIDGE.md, Video (2.0)).</summary>
public static class VideoCodes
{
    /// <summary>The error code a method answers when a source or encoder cannot start (one of the seven video error codes).</summary>
    public static string Of(VideoErrorCode code) => code switch
    {
        VideoErrorCode.ScreenCapturePrivacyDenied or VideoErrorCode.CaptureUnsupported => DomainErrorCodes.VideoCaptureBlocked,
        VideoErrorCode.ProtectedContent => DomainErrorCodes.VideoProtectedContent,
        VideoErrorCode.CameraPrivacyDenied => DomainErrorCodes.VideoCameraPrivacy,
        VideoErrorCode.CameraInUse => DomainErrorCodes.VideoCameraInUse,
        VideoErrorCode.EncoderUnavailable or VideoErrorCode.EncoderFailed or VideoErrorCode.NoGraphicsDevice => DomainErrorCodes.VideoEncoderUnavailable,
        VideoErrorCode.DiskFull => DomainErrorCodes.VideoDiskFull,
        _ => DomainErrorCodes.VideoSourceNotFound,
    };

    /// <summary>The <c>recording.videoProblem</c> code for something that happened to a running track of <paramref name="kind"/>.</summary>
    public static string Problem(VideoErrorCode code, VideoSourceKind kind) => (code, kind) switch
    {
        (VideoErrorCode.SourceLost, VideoSourceKind.Display) => VideoProblemCodes.DisplayDisconnected,
        (VideoErrorCode.SourceLost, VideoSourceKind.Window) => VideoProblemCodes.WindowClosed,
        (VideoErrorCode.SourceMinimised, _) => VideoProblemCodes.WindowMinimised,
        (VideoErrorCode.EncoderFailed, _) => VideoProblemCodes.EncoderFallback,
        _ => Of(code),
    };

    public static string Of(VideoSessionState state) => state switch
    {
        VideoSessionState.Paused => "paused",
        VideoSessionState.Waiting => "waiting",
        VideoSessionState.Ended => "ended",
        VideoSessionState.Stopped => "stopped",
        _ => "recording",
    };
}
