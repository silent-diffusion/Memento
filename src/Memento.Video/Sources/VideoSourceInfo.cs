namespace Memento.Video.Sources;

/// <summary>A video source as the picker lists it (DESIGN.md §18.2).</summary>
/// <param name="Name">"Display 1", the window's title, the camera's name.</param>
/// <param name="Detail">The sub-line: "2560 × 1440 · 60 Hz · primary", "Zoom · 1600 × 900", "Camera".</param>
/// <param name="Width">Pixels (0 when not known without opening the device).</param>
/// <param name="IsExcluded">Memento's own windows: listed but not selectable ("This app is excluded from capture").</param>
/// <param name="ProcessName">For windows: the program that owns it.</param>
/// <param name="IsProtected">A window that keeps itself out of captures (display affinity): listed, excluded, never read.</param>
public sealed record VideoSourceInfo(
    VideoSourceId Id,
    string Name,
    string Detail,
    int Width,
    int Height,
    int RefreshRate,
    bool IsPrimary,
    bool IsExcluded,
    string? ProcessName = null,
    bool IsProtected = false);
