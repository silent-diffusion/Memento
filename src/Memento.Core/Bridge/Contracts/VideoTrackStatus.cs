namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// One video track while recording: the Encoder card, the video lane and the footer (DESIGN.md §18.1).
/// </summary>
/// <param name="TrackId"><c>screen</c>, <c>camera</c>; <c>screen-2</c> when the role was turned off and on again.</param>
/// <param name="Role"><c>screen</c> or <c>camera</c>.</param>
/// <param name="State"><c>recording</c>, <c>paused</c> (the recording is paused), <c>waiting</c> (window minimised, display disconnected), <c>ended</c> (the track stopped early) or <c>stopped</c>.</param>
/// <param name="Reason">The §18.6 sentence while waiting or after the track ended; otherwise <c>null</c>.</param>
/// <param name="Encoder">"NVIDIA H.264 Encoder MFT", "Microsoft H.264 software encoder".</param>
/// <param name="Hardware">"Hardware · GPU" when true, "Software · CPU" otherwise.</param>
/// <param name="ActualBitsPerSecond">Bytes written over the recorded time, so far.</param>
/// <param name="StartOffsetMs">Where the track's first frame sits on the recording timeline.</param>
public sealed record VideoTrackStatus(
    string TrackId,
    string Role,
    string State,
    string SourceId,
    string SourceName,
    string? Reason,
    string Encoder,
    bool Hardware,
    int Width,
    int Height,
    int FrameRate,
    long TargetBitsPerSecond,
    long ActualBitsPerSecond,
    long FramesWritten,
    long FramesDropped,
    long BytesWritten,
    long DurationMs,
    long StartOffsetMs);
