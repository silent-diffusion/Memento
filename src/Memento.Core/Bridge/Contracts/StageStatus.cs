namespace Memento.Core.Bridge.Contracts;

/// <summary>A status pill (DESIGN.md §5.4).</summary>
/// <param name="Stage">Stage name: <c>transcript</c>, <c>speakers</c>, <c>chapters</c> or <c>minutes</c>.</param>
/// <param name="State"><c>done</c>, <c>active</c>, <c>queued</c> or <c>failed</c>.</param>
/// <param name="Percent">Progress 0–100 while <c>active</c>; otherwise <c>null</c>.</param>
public sealed record StageStatus(string Stage, string State, int? Percent);
