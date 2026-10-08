namespace Memento.Core.Export;

/// <summary><c>exports-running.json</c>: the exports that have started and not yet finished.</summary>
public sealed record ExportJournalDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<ExportJournalEntry> Entries { get; init; } = [];
}
