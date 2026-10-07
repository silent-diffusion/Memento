namespace Memento.Documents.Agenda;

/// <summary>What <see cref="FormatSniffer"/> found: the kind and a user-facing description ("a PNG image").</summary>
internal sealed record SniffResult(AgendaSourceKind Kind, string Description);
