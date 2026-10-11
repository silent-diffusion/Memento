namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// A display, window or camera the Recording screen's VIDEO section and the picker list (BRIDGE.md, Video (2.0),
/// DESIGN.md §18.1–§18.2). Listing opens nothing: no camera light, no capture border.
/// </summary>
/// <param name="Id"><c>display:\\.\DISPLAY1</c>, <c>window:1A2B3C</c> or <c>camera:&lt;symbolic link&gt;</c>; stable for the session.</param>
/// <param name="Kind"><c>display</c>, <c>window</c> or <c>camera</c>.</param>
/// <param name="Name">"Display 1", the window's title, the camera's name.</param>
/// <param name="Detail">The sub-line: "2560 × 1440 · 60 Hz · primary", "Zoom · 1600 × 900", "This app is excluded from capture".</param>
/// <param name="Width">Pixels; 0 for a camera (not known without opening it).</param>
/// <param name="Excluded">Listed but not selectable: Memento's own windows, and windows that keep themselves out of captures.</param>
/// <param name="Protected">The window asks Windows to keep it out of captures (protected players, some password managers).</param>
/// <param name="ProcessName">For a window: the program that owns it; otherwise <c>null</c>.</param>
public sealed record VideoSource(
    string Id,
    string Kind,
    string Name,
    string Detail,
    int Width,
    int Height,
    int RefreshRate,
    bool Primary,
    bool Excluded,
    bool Protected,
    string? ProcessName);
