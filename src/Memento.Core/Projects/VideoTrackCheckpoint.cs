namespace Memento.Core.Projects;

/// <summary>
/// A video track's entry in <c>recording.state.json</c> (<c>videoTracks[]</c>), written at every recording checkpoint
/// after the file was flushed: enough for <c>VideoRecovery</c> (Memento.Video) to find and repair every part after a
/// crash without the manifest.
/// </summary>
/// <param name="Parts">Relative paths of the files written so far, in order, with where each starts.</param>
/// <param name="BytesAtCheckpoint">Size of the last part when it was flushed.</param>
public sealed record VideoTrackCheckpoint(
    string TrackId,
    string Role,
    string SourceKind,
    string SourceId,
    string Name,
    int Width,
    int Height,
    int FrameRate,
    IReadOnlyList<VideoTrackPart> Parts,
    long BytesAtCheckpoint,
    long FramesWritten,
    long FramesDropped,
    long? EndedAtMs,
    string? EndReason);
