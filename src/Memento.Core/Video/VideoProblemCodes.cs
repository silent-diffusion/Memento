using Memento.Core.Bridge;

namespace Memento.Core.Video;

/// <summary>
/// The closed list of <c>recording.videoProblem</c> codes (BRIDGE.md, Video (2.0)). Seven are also error codes
/// (<see cref="DomainErrorCodes"/>); four describe a state the track is in, not an error, and are never a method's error:
/// <see cref="DisplayDisconnected"/>, <see cref="WindowClosed"/>, <see cref="WindowMinimised"/> and <see cref="EncoderFallback"/>.
/// </summary>
public static class VideoProblemCodes
{
    /// <summary>The captured display went away; the track waits (state <c>waiting</c>).</summary>
    public const string DisplayDisconnected = "video.displayDisconnected";

    /// <summary>The captured window closed; the track ends there (<c>ended</c>).</summary>
    public const string WindowClosed = "video.windowClosed";

    /// <summary>The captured window was minimised; frames resume by themselves (<c>waiting</c>, footer only).</summary>
    public const string WindowMinimised = "video.windowMinimised";

    /// <summary>The hardware encoder failed; the track continues in software (<c>recording</c>; <c>detail</c> the software encoder).</summary>
    public const string EncoderFallback = "video.encoderFallback";

    public const string ProtectedContent = DomainErrorCodes.VideoProtectedContent;
    public const string DiskFull = DomainErrorCodes.VideoDiskFull;
    public const string CameraInUse = DomainErrorCodes.VideoCameraInUse;
    public const string CameraPrivacy = DomainErrorCodes.VideoCameraPrivacy;
    public const string CaptureBlocked = DomainErrorCodes.VideoCaptureBlocked;
    public const string SourceNotFound = DomainErrorCodes.VideoSourceNotFound;
    public const string EncoderUnavailable = DomainErrorCodes.VideoEncoderUnavailable;

    public static IReadOnlyList<string> All { get; } =
        [DisplayDisconnected, WindowClosed, WindowMinimised, ProtectedContent, EncoderFallback, DiskFull, CameraInUse, CameraPrivacy, CaptureBlocked, SourceNotFound, EncoderUnavailable];

    /// <summary>The track's state that goes with a code (BRIDGE.md's table).</summary>
    public static string StateOf(string code) => code switch
    {
        DisplayDisconnected or WindowMinimised or CameraInUse => "waiting",
        WindowClosed or ProtectedContent or SourceNotFound => "ended",
        EncoderFallback => "recording",
        _ => "stopped",
    };
}
