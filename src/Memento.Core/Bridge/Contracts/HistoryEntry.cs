namespace Memento.Core.Bridge.Contracts;

/// <summary>One line of the History tab (DESIGN.md §9).</summary>
/// <param name="Stage">
/// A stage name (<c>stored</c>, <c>optimize</c>; from M2 <c>transcript</c>, <c>speakers</c>, <c>minutes</c>) or
/// <c>recorded</c>, <c>recovered</c>, <c>edited</c>.
/// </param>
/// <param name="Event"><c>started</c>, <c>completed</c>, <c>failed</c> or <c>info</c>.</param>
/// <param name="Detail">Engine, model, device, duration, what was sent.</param>
public sealed record HistoryEntry(DateTimeOffset At, string Stage, string Event, string Summary, string? Detail);
