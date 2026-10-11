namespace Memento.Core.Video;

/// <summary>A recorded video file an export reads.</summary>
/// <param name="StartOffsetMs">Where its first frame sits on the recording timeline.</param>
public sealed record VideoComposeInput(string Path, long StartOffsetMs, long DurationMs, int Width, int Height, int FrameRate, int BitrateBitsPerSecond);
