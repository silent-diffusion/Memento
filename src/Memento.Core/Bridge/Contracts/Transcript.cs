namespace Memento.Core.Bridge.Contracts;

/// <summary>A recording's transcript (<c>transcript.json</c>, schema v1; BRIDGE.md M2).</summary>
/// <param name="Language">BCP-47 primary subtag, e.g. <c>en</c>.</param>
/// <param name="LanguageDetected">The engine chose the language (Settings said "auto").</param>
/// <param name="Version">Increments on every write.</param>
/// <param name="LowConfidenceThreshold">Words below it are marked; from Settings when the transcript was made.</param>
/// <param name="CoverageGaps">Speech with no transcript, for the "may be missing" marks (proposed for BRIDGE.md).</param>
public sealed record Transcript(
    int SchemaVersion,
    string Language,
    bool LanguageDetected,
    TranscriptEngineInfo Engine,
    IReadOnlyList<Speaker> Speakers,
    IReadOnlyList<TranscriptSegment> Segments,
    bool Reviewed,
    int Version,
    double LowConfidenceThreshold,
    IReadOnlyList<CoverageGap> CoverageGaps);
