namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>history.links</c>: one entry per History line that changed the transcript or a document.</summary>
public sealed record HistoryLinksResult(IReadOnlyList<HistoryLink> Links);
