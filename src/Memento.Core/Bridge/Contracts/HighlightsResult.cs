namespace Memento.Core.Bridge.Contracts;

/// <summary>Every highlight of the recording after the change, ordered by time.</summary>
public sealed record HighlightsResult(IReadOnlyList<Highlight> Highlights);
