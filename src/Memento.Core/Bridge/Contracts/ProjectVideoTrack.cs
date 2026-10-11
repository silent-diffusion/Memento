namespace Memento.Core.Bridge.Contracts;

/// <summary>One video track Review plays (<see cref="ProjectVideo.Tracks"/>).</summary>
/// <param name="Id"><c>screen</c> or <c>camera</c>.</param>
/// <param name="SourceName">The last source when it was switched.</param>
/// <param name="StartOffsetMs">Where the track begins on the recording timeline: video time = mix time − this.</param>
/// <param name="EndReason">The §18.6 sentence when it ended before the recording did; otherwise <c>null</c>.</param>
public sealed record ProjectVideoTrack(
    string Id,
    string SourceKind,
    string SourceName,
    int Width,
    int Height,
    int FrameRate,
    string Encoder,
    bool Hardware,
    long FramesWritten,
    long FramesDropped,
    long SizeBytes,
    long StartOffsetMs,
    long DurationMs,
    string? EndReason,
    IReadOnlyList<VideoPart> Parts);
