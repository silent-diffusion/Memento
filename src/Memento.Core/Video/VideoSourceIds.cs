using Memento.Core.Projects;

namespace Memento.Core.Video;

/// <summary>Reads the kind and role of a video source id (<c>display:…</c>, <c>window:…</c>, <c>camera:…</c>) without Memento.Video.</summary>
public static class VideoSourceIds
{
    public const string DisplayPrefix = "display:";
    public const string WindowPrefix = "window:";
    public const string CameraPrefix = "camera:";

    /// <summary>Whether <paramref name="id"/> names a display, window or camera (as opposed to an audio source).</summary>
    public static bool IsVideo(string? id) => KindOf(id) is not null;

    /// <summary><c>display</c>, <c>window</c>, <c>camera</c>, or <c>null</c> for anything else.</summary>
    public static string? KindOf(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (id.StartsWith(DisplayPrefix, StringComparison.Ordinal) && id.Length > DisplayPrefix.Length)
        {
            return "display";
        }

        if (id.StartsWith(WindowPrefix, StringComparison.Ordinal) && id.Length > WindowPrefix.Length)
        {
            return "window";
        }

        return id.StartsWith(CameraPrefix, StringComparison.Ordinal) && id.Length > CameraPrefix.Length ? "camera" : null;
    }

    /// <summary><c>camera</c> for a camera, <c>screen</c> for a display or a window, <c>null</c> otherwise.</summary>
    public static string? RoleOf(string? id) => KindOf(id) switch
    {
        null => null,
        "camera" => VideoTrack.RoleCamera,
        _ => VideoTrack.RoleScreen,
    };
}
