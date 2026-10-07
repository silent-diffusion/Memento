namespace Memento.Documents.Agenda.Text;

/// <summary>What <see cref="AgendaStructurer"/> made of a list of lines.</summary>
internal sealed record StructuredAgenda(
    IReadOnlyList<ParsedAgendaItem> Items,
    string? Title,
    IReadOnlyList<AgendaParseWarning> Warnings);
