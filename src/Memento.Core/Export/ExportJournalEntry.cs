namespace Memento.Core.Export;

/// <summary>One running export in <see cref="ExportJournal"/>: what to remove if Memento never gets to finish it.</summary>
/// <param name="Written">Files already moved into the destination.</param>
/// <param name="CreatedFolders">Folders the export created (removed again when left empty).</param>
public sealed record ExportJournalEntry(
    string JobId,
    string RecordingId,
    string Title,
    DateTimeOffset StartedAt,
    string? OutputFolder,
    string? Work,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> CreatedFolders)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    public IReadOnlyList<string> Written { get; set; } = Written ?? [];

    public IReadOnlyList<string> CreatedFolders { get; set; } = CreatedFolders ?? [];
}
