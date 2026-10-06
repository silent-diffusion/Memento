namespace Memento.Core.Bridge.Contracts;

/// <summary>One recorded source.</summary>
/// <param name="File">Relative to the project folder, e.g. <c>tracks/mic.flac</c> (<c>.wav</c> while recording).</param>
/// <param name="Sha256">Set at finalize.</param>
/// <param name="StartOffsetMs">0 for tracks that started with the session; where a track added mid-session begins on the timeline.</param>
/// <param name="EndedEarlyAtMs">When the track stopped before the session did (turned off or device lost).</param>
public sealed record Track(
    string Id,
    string SourceId,
    string SourceKind,
    string Name,
    string File,
    int SampleRate,
    int Channels,
    long DurationMs,
    string? Sha256,
    long StartOffsetMs,
    long? EndedEarlyAtMs);
