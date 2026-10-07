namespace Memento.AI.Payload;

/// <summary>An agenda item: its number as shown ("1", "2.a"), title and optional notes.</summary>
public sealed record PayloadAgendaItem(string Number, string Title, string? Notes = null);
