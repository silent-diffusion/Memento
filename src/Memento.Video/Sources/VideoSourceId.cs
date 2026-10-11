using System.Globalization;

namespace Memento.Video.Sources;

/// <summary>
/// A stable id for a video source: <c>display:\\.\DISPLAY1</c> (the GDI device name survives re-plugging, a monitor
/// handle does not), <c>window:&lt;hwnd hex&gt;</c> (a window lives only as long as it is open), or
/// <c>camera:&lt;symbolic link&gt;</c> (Media Foundation) or <c>camera:dshow:&lt;device path, filter CLSID or name&gt;</c>
/// (DirectShow, <see cref="CameraListMerger.DirectShowKey"/>).
/// </summary>
public readonly record struct VideoSourceId(VideoSourceKind Kind, string Key)
{
    public static VideoSourceId Display(string deviceName) => new(VideoSourceKind.Display, deviceName);

    public static VideoSourceId Window(IntPtr hwnd) => new(VideoSourceKind.Window, ((long)hwnd).ToString("X", CultureInfo.InvariantCulture));

    public static VideoSourceId Camera(string symbolicLink) => new(VideoSourceKind.Camera, symbolicLink);

    /// <summary>How a camera id is opened; <c>null</c> for a display or a window.</summary>
    public CameraBackend? Backend =>
        Kind != VideoSourceKind.Camera ? null
        : Key.StartsWith(CameraListMerger.DirectShowPrefix, StringComparison.Ordinal) ? CameraBackend.DirectShow
        : CameraBackend.MediaFoundation;

    /// <summary>The window handle of a <see cref="VideoSourceKind.Window"/> id.</summary>
    public IntPtr WindowHandle =>
        Kind == VideoSourceKind.Window && long.TryParse(Key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? new IntPtr(value) : IntPtr.Zero;

    public override string ToString() => Kind switch
    {
        VideoSourceKind.Display => "display:" + Key,
        VideoSourceKind.Window => "window:" + Key,
        _ => "camera:" + Key,
    };

    /// <summary>Parses <see cref="ToString"/>'s form.</summary>
    public static bool TryParse(string? text, out VideoSourceId id)
    {
        id = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == text.Length - 1)
        {
            return false;
        }

        var key = text[(colon + 1)..];
        switch (text[..colon])
        {
            case "display":
                id = Display(key);
                return true;
            case "window":
                if (!long.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                {
                    return false;
                }

                id = new VideoSourceId(VideoSourceKind.Window, key.ToUpperInvariant());
                return true;
            case "camera":
                id = Camera(key);
                return true;
            default:
                return false;
        }
    }
}
