using Memento.Core.Bridge;
using Memento.Video.Recording;

namespace Memento.Video.Hosting;

/// <summary>The bridge's names for the library's error codes and session states (BRIDGE.md, Video (2.0)).</summary>
public static class VideoCodes
{
    public static string Of(VideoErrorCode code) => code switch
    {
        VideoErrorCode.SourceLost => DomainErrorCodes.VideoSourceLost,
        VideoErrorCode.SourceNotFound => DomainErrorCodes.VideoSourceNotFound,
        VideoErrorCode.ScreenCapturePrivacyDenied => DomainErrorCodes.VideoCaptureBlocked,
        VideoErrorCode.ProtectedContent => DomainErrorCodes.VideoProtectedContent,
        VideoErrorCode.CameraPrivacyDenied => DomainErrorCodes.VideoCameraPrivacy,
        VideoErrorCode.CameraInUse => DomainErrorCodes.VideoCameraInUse,
        VideoErrorCode.EncoderUnavailable => DomainErrorCodes.VideoEncoderUnavailable,
        VideoErrorCode.EncoderFailed => DomainErrorCodes.VideoEncoderFailed,
        VideoErrorCode.DiskFull => DomainErrorCodes.VideoDiskFull,
        VideoErrorCode.WriteFailed => DomainErrorCodes.VideoWriteFailed,
        _ => DomainErrorCodes.VideoUnavailable,
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
