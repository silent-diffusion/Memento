using Memento.Core.Formatting;

namespace Memento.Video;

/// <summary>
/// The sentences of DESIGN.md §18.6: the thing, the time, what is safe, then the fix. Video fails first and alone, so
/// every sentence says that audio keeps recording. Times are recording positions in milliseconds.
/// </summary>
public static class VideoErrors
{
    public static string DisplayDisconnected(string display, long? atMs = null) =>
        $"{display} disconnected{At(atMs)}. Video is paused; audio and the camera keep recording. Switch the screen track to another display, or keep waiting for it to come back.";

    public static string WindowClosed(string window, long? atMs = null) =>
        $"“{window}” was closed{At(atMs)}. The video track ends there and everything before it is saved. Audio continues. Capture a display instead, or record audio only from here.";

    public static string DisplayNotFound(string display) =>
        $"{display} is not connected, so it can't be captured. Audio recording is not affected. Pick another display.";

    public static string WindowNotFound(string window) =>
        $"“{window}” is no longer open, so it can't be captured. Audio recording is not affected. Pick another window or a display.";

    public static string ProtectedWindow(string window) =>
        $"“{window}” shows protected content that Windows does not allow to be recorded, so its video track is stopped rather than recorded as black. Audio keeps recording. Capture a display instead, or record audio only.";

    public static string CaptureUnsupported() =>
        "Screen capture is not available on this version of Windows or in this session. Audio sources are not affected. Update Windows, or record audio only.";

    public static string ScreenCaptureBlocked() =>
        "Screen capture is blocked for Memento in Windows privacy settings. Audio sources are not affected. Allow it under Privacy & security › Screen capture, then come back, or record audio only.";

    public static string CameraPrivacyOff(string camera) =>
        $"{camera} is blocked by Windows privacy settings. Recording continues with the screen and all audio tracks. Allow camera access for desktop apps under Privacy & security › Camera, then retry the camera.";

    public static string CameraInUse(string camera, string? otherApp) =>
        (otherApp is null ? $"{camera} is in use by another app." : $"{camera} is in use by {otherApp}.")
        + " Recording continues with the screen and all audio tracks. The camera track will start the moment it becomes free. Retry the camera, continue without it, or pick another camera.";

    public static string CameraNotFound(string camera) =>
        $"{camera} is not connected. Recording continues with the screen and all audio tracks. Connect it and retry, or pick another camera.";

    public static string CameraDisconnected(string camera, long? atMs = null) =>
        $"{camera} was disconnected{At(atMs)}. The camera track ends there and everything before it is saved; the screen and audio keep recording. Connect it again to start a new camera track.";

    public static string HardwareEncoderFailed(long? atMs = null) =>
        $"Hardware encoder failed{At(atMs)}. Switched to software encoding; frames before it are saved. CPU use is higher until this recording ends. Lower the quality if the PC is slow.";

    public static string NoEncoder() =>
        "Video can't be recorded: no H.264 encoder could be opened, on the graphics card or in software. Audio recording is not affected. Install the Windows Media Feature Pack (Windows N editions) or update the graphics driver.";

    public static string DiskFull(string track, long? atMs = null) =>
        $"The disk is full: the {track} video track stopped{At(atMs)}. Everything recorded before is saved and audio keeps recording. Free up space to record video again.";

    public static string WriteFailed(string track, string reason, long? atMs = null) =>
        $"The {track} video track could not be written{At(atMs)} ({reason}). Everything recorded before is saved and audio keeps recording. Check that the library folder is still reachable.";

    private static string At(long? atMs) => atMs is null ? string.Empty : " at " + HumanFormat.Clock(atMs.Value);
}
