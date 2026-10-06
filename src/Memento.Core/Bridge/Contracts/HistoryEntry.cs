namespace Memento.Core.Bridge.Contracts;

/// <summary>One line of the History tab (DESIGN.md §9).</summary>
/// <param name="Stage">A stage name, or <c>recorded</c>, <c>edited</c>, <c>exported</c>, <c>recovered</c>.</param>
/// <param name="Event"><c>started</c>, <c>progress</c>, <c>completed</c>, <c>failed</c> or <c>info</c>.</param>
/// <param name="Detail">Engine, model, device, duration, what was sent.</param>
public sealed record HistoryEntry(DateTimeOffset At, string Stage, string Event, string Summary, string? Detail);
