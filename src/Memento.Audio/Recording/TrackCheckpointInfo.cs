namespace Memento.Audio.Recording;

/// <summary>One active track at a checkpoint.</summary>
/// <param name="SourceId">The bridge source id.</param>
/// <param name="FileStem">File stem in <c>tracks/</c>.</param>
/// <param name="Parts">WAV parts on disk.</param>
/// <param name="BytesWritten">Audio bytes durable on disk.</param>
/// <param name="Frames">Frames durable on disk.</param>
/// <param name="Duration">Duration durable on disk.</param>
/// <param name="DriftPpm">Device clock vs QPC so far, or null if under a second of reliable timestamps.</param>
/// <param name="OverrunFrames">Frames the capture dropped because writing fell behind (covered with silence).</param>
public sealed record TrackCheckpointInfo(
    string SourceId,
    string FileStem,
    IReadOnlyList<string> Parts,
    long BytesWritten,
    long Frames,
    TimeSpan Duration,
    double? DriftPpm,
    long OverrunFrames);
