namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.versions</c>, newest first. Empty when version history is off.</summary>
public sealed record TranscriptVersionsResult(IReadOnlyList<TranscriptVersion> Versions);
