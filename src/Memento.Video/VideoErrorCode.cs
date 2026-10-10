namespace Memento.Video;

/// <summary>Why a video source or track stopped or could not start (DESIGN.md §18.6). Audio is never affected by any of them.</summary>
public enum VideoErrorCode
{
    /// <summary>The display was disconnected or the captured window was closed.</summary>
    SourceLost,

    /// <summary>The display, window or camera chosen is not there (any more) when the track starts.</summary>
    SourceNotFound,

    /// <summary>Windows.Graphics.Capture is not available on this Windows (or in this session, e.g. a service).</summary>
    CaptureUnsupported,

    /// <summary>Windows privacy settings block screen capture for Memento.</summary>
    ScreenCapturePrivacyDenied,

    /// <summary>The window shows protected (DRM) content, which Windows blacks out; the track is stopped rather than filled with black.</summary>
    ProtectedContent,

    /// <summary>Settings › Privacy &amp; security › Camera is off for desktop apps or for the PC.</summary>
    CameraPrivacyDenied,

    /// <summary>Another app holds the camera.</summary>
    CameraInUse,

    /// <summary>No H.264 encoder could be opened, neither on the graphics card nor in software.</summary>
    EncoderUnavailable,

    /// <summary>The hardware encoder failed while recording; the track switches to software.</summary>
    EncoderFailed,

    /// <summary>The disk is full (or below the floor); video stops first.</summary>
    DiskFull,

    /// <summary>Writing the video file failed for another reason (folder gone, access denied).</summary>
    WriteFailed,

    /// <summary>No usable Direct3D 11 graphics adapter.</summary>
    NoGraphicsDevice,
}
