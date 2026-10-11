namespace Memento.Video.Sources;

/// <summary>
/// Turns what Media Foundation returns for a camera into the §18.6 sentences. Pure, so every case is unit-tested
/// without a camera (ENGINE-NOTES.md §M.2: this PC has none).
/// </summary>
public static class CameraErrors
{
    public const int AccessDenied = unchecked((int)0x80070005);
    public const int Busy = unchecked((int)0x800700AA);
    public const int DeviceNotConnected = unchecked((int)0x8007048F);
    public const int DeviceRemoved = unchecked((int)0x80070651);

    /// <summary><c>MF_E_HW_MFT_FAILED_START_STREAMING</c>: the driver could not start, typically because another app streams.</summary>
    public const int FailedStartStreaming = unchecked((int)0xC00D3704);

    /// <summary><c>MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED</c>: the camera went away while streaming.</summary>
    public const int DeviceInvalidated = unchecked((int)0xC00D3E84);

    /// <summary><c>MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED</c>: another app took the camera.</summary>
    public const int DevicePreempted = unchecked((int)0xC00D3E85);

    /// <summary><c>MF_E_NO_CAPTURE_DEVICES_AVAILABLE</c>.</summary>
    public const int NoCaptureDevices = unchecked((int)0xC00D4A3C);

    /// <summary>
    /// The exception for <paramref name="hresult"/>. <paramref name="access"/> is read from the privacy switches when it
    /// happens, so a denial says which switch; <paramref name="otherApp"/> names who holds the camera when Windows knows.
    /// </summary>
    public static VideoCaptureException Classify(int hresult, string camera, CameraAccess access, string? otherApp, long? atMs = null, Exception? inner = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        VideoCaptureException Make(VideoErrorCode code, string message) =>
            inner is null ? new VideoCaptureException(code, message) : new VideoCaptureException(code, message, inner);

        if (hresult == AccessDenied || access is CameraAccess.DeniedForPc or CameraAccess.DeniedForUser or CameraAccess.DeniedForDesktopApps)
        {
            var where = CameraPrivacy.Where(access);
            var denied = Make(VideoErrorCode.CameraPrivacyDenied, $"{camera} is blocked by Windows privacy settings. Recording continues with the screen and all audio tracks. Turn on {where}, then retry the camera.");
            return new VideoCaptureException(denied.Code, denied.Message, denied.InnerException ?? denied) { Detail = where };
        }

        return hresult switch
        {
            Busy or FailedStartStreaming or DevicePreempted => inner is null
                ? new VideoCaptureException(VideoErrorCode.CameraInUse, VideoErrors.CameraInUse(camera, otherApp)) { Detail = otherApp }
                : new VideoCaptureException(VideoErrorCode.CameraInUse, VideoErrors.CameraInUse(camera, otherApp), inner) { Detail = otherApp },
            DeviceInvalidated or DeviceNotConnected or DeviceRemoved => Make(VideoErrorCode.SourceLost, VideoErrors.CameraDisconnected(camera, atMs)),
            NoCaptureDevices => Make(VideoErrorCode.SourceNotFound, VideoErrors.CameraNotFound(camera)),
            _ => Make(VideoErrorCode.SourceNotFound, $"{camera} could not be started (0x{hresult:X8}). Recording continues with the screen and all audio tracks. Reconnect the camera and retry, or pick another camera."),
        };
    }
}
