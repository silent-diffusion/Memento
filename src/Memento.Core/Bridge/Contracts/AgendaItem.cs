namespace Memento.Core.Bridge.Contracts;

/// <summary>One agenda line.</summary>
public sealed record AgendaItem(string Id, string Text, bool Covered, bool Uncertain, string? UncertainReason);
