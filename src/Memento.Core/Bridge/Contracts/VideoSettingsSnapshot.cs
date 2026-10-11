namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Recording › Video defaults (DESIGN.md §18.5), <c>recording.video</c>. Changes apply to the next recording.</summary>
/// <param name="DefaultSource"><c>screen</c>, <c>window</c>, <c>camera</c> or <c>screenAndCamera</c> (the default).</param>
/// <param name="Resolution"><c>display</c> (match the display), <c>1080p</c> or <c>720p</c>.</param>
/// <param name="FrameRate">24, 30 or 60.</param>
/// <param name="Quality"><c>smaller</c>, <c>balanced</c> or <c>best</c>: about 6, 12 and 20 Mb/s at 1440p 30 fps.</param>
/// <param name="DefaultCameraId">The camera the Camera row starts with; <c>null</c> for the first one.</param>
/// <param name="PipCorner">Where the camera inset starts.</param>
/// <param name="HardwareEncoder">Use the graphics card's encoder; falls back to software automatically, with a notice.</param>
public sealed record VideoSettingsSnapshot(
    string DefaultSource,
    string Resolution,
    int FrameRate,
    string Quality,
    string? DefaultCameraId,
    string PipCorner,
    bool HardwareEncoder,
    bool IncludePointer);
