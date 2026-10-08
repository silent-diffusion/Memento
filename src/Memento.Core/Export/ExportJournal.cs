using System.Text.Json;

namespace Memento.Core.Export;

/// <summary>
/// The exports running now, written to a small JSON file each time a file lands in the destination, so an export cut
/// short by a crash or a power cut is cleaned up at the next launch exactly as a failed one is (its files removed,
/// the project untouched). Writes are atomic; an unreadable file counts as empty.
/// </summary>
public sealed class ExportJournal(ExportJournalOptions options)
{
    private readonly object _gate = new();

    public IReadOnlyList<ExportJournalEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return Load().Entries;
            }
        }
    }

    public void Set(ExportJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            var entries = Load().Entries.Where(e => e.JobId != entry.JobId).Append(entry).ToList();
            Save(new ExportJournalDocument { Entries = entries });
        }
    }

    public void Remove(string jobId)
    {
        lock (_gate)
        {
            var document = Load();
            if (document.Entries.All(e => e.JobId != jobId))
            {
                return;
            }

            var entries = document.Entries.Where(e => e.JobId != jobId).ToList();
            if (entries.Count == 0)
            {
                File.Delete(options.Path);
                return;
            }

            Save(new ExportJournalDocument { Entries = entries });
        }
    }

    private ExportJournalDocument Load()
    {
        try
        {
            if (!File.Exists(options.Path))
            {
                return new ExportJournalDocument();
            }

            return JsonSerializer.Deserialize(File.ReadAllBytes(options.Path), ExportJsonContext.Default.ExportJournalDocument) ?? new ExportJournalDocument();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ExportJournalDocument();
        }
    }

    private void Save(ExportJournalDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.Path)!);
        var temporary = options.Path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(document, ExportJsonContext.Default.ExportJournalDocument));
        File.Move(temporary, options.Path, overwrite: true);
    }
}
