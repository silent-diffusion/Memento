namespace Memento.Core.Projects;

/// <summary>One track in <c>recording.state.json</c>: enough to repair and finalize it without the manifest.</summary>
/// <param name="File">Relative capture file, e.g. <c>tracks/mic.wav</c>.</param>
/// <param name="SampleEncoding"><c>pcm</c> or <c>float</c>.</param>
/// <param name="BytesAtCheckpoint">Sample bytes the header on disk covered at the last checkpoint.</param>
public sealed record RecordingStateTrack(
    string TrackId,
    string SourceId,
    string SourceKind,
    string Name,
    string File,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    string SampleEncoding,
    long StartOffsetMs,
    long BytesAtCheckpoint,
    long? EndedAtMs,
    string? EndReason);
