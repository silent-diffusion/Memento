namespace Memento.Core.Bridge.Contracts;

/// <summary>A status pill (DESIGN.md §5.4) or a processing-card column.</summary>
/// <param name="Stage">Stage name, in pipeline order: <c>stored</c>, <c>transcript</c>, <c>speakers</c>, <c>minutes</c>, <c>optimize</c>.</param>
/// <param name="State"><c>done</c>, <c>active</c>, <c>queued</c> or <c>failed</c>.</param>
/// <param name="Percent">Progress 0–100 while <c>active</c>; otherwise <c>null</c>.</param>
/// <param name="Label">Status text, e.g. <c>"64% · local GPU"</c>, <c>"Done"</c>, <c>"Queued"</c>, <c>"Transcript failed"</c>.</param>
public sealed record StageStatus(string Stage, string State, int? Percent, string? Label);
