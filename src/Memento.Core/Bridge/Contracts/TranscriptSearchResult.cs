namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.search</c>, in transcript order.</summary>
public sealed record TranscriptSearchResult(IReadOnlyList<TranscriptMatch> Matches);
