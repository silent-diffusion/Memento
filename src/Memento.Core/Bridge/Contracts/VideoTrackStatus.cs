namespace Memento.Core.Bridge.Contracts;

/// <summary>One video track while recording: the Encoder card, the video lane and the footer (DESIGN.md §18.1).</summary>
/// <param name="TrackId"><c>screen</c> or <c>camera</c>: the role; a role turned off and on again is still the same id here.</param>
/// <param name="State"><c>recording</c>, <c>paused</c> (the recording is paused), <c>waiting</c> (window minimised, display disconnected, camera busy), <c>ended</c> (the track stopped early) or <c>stopped</c>.</param>
/// <param name="SourceId">The source the track captures now.</param>
/// <param name="Reason">The §18.6 sentence while waiting or after the track ended; otherwise <c>null</c>.</param>
/// <param name="Encoder">"NVIDIA H.264 Encoder MFT", "Microsoft H.264 software encoder".</param>
/// <param name="Hardware">"Hardware · GPU" when true, "Software · CPU" otherwise.</param>
/// <param name="ActualBitsPerSecond">Bytes written over the recorded time, so far.</param>
public sealed record VideoTrackStatus(
    string TrackId,
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
    long DurationMs);
