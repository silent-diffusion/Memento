namespace Memento.Core.Bridge.Contracts;

/// <summary>One hit of <c>transcript.search</c>.</summary>
/// <param name="Start">Segment start, seconds.</param>
/// <param name="Snippet">The text around the match, shortened with "…".</param>
public sealed record TranscriptMatch(string SegmentId, double Start, string Snippet);
