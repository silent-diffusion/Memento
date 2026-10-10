namespace Memento.Video.Sources;

public enum CameraAccess
{
    /// <summary>No switch is recorded (a PC that never used a camera); opening the camera will tell.</summary>
    Unknown,

    Allowed,

    /// <summary>Camera access is off for the whole PC (set by an administrator).</summary>
    DeniedForPc,

    /// <summary>"Let apps access your camera" is off.</summary>
    DeniedForUser,

    /// <summary>"Let desktop apps access your camera" is off.</summary>
    DeniedForDesktopApps,
}
