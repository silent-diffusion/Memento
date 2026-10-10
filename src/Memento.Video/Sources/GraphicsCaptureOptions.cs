namespace Memento.Video.Sources;

/// <summary>Options for display and window capture.</summary>
/// <param name="IncludeCursor">Settings › Recording › Include the mouse pointer (on by default).</param>
/// <param name="ShowBorder">Keep Windows' yellow capture border. Off by default: Memento's own Recording screen says what is captured.</param>
public sealed record GraphicsCaptureOptions(bool IncludeCursor = true, bool ShowBorder = false);
