using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Export;

/// <summary>
/// At launch, before any export can start: an export that was still running when Memento closed (crash, power cut,
/// killed) is cleaned up as a failed one would have been. The files it had moved into the destination are removed,
/// its hidden work folder goes, folders it created and left empty go, and the recording's History says so.
/// </summary>
public sealed partial class InterruptedExports(ExportJournal journal, IProjectStore store, TimeProvider time, ILogger<InterruptedExports> logger)
{
    private readonly ILogger<InterruptedExports> _logger = logger;

    /// <returns>How many exports were cleaned up.</returns>
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken)
    {
        var entries = journal.Entries;
        foreach (var entry in entries)
        {
            foreach (var path in entry.Written)
            {
                Quietly(() => File.Delete(path), path);
            }

            if (entry.Work is { } work)
            {
                Quietly(() => Directory.Delete(work, recursive: true), work);
            }

            foreach (var folder in entry.CreatedFolders.Reverse())
            {
                Quietly(() => { if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }, folder);
            }

            LogCleaned(entry.JobId, entry.RecordingId, entry.Written.Count);
            if (store.Exists(entry.RecordingId))
            {
                try
                {
                    await store.AppendHistoryAsync(
                        entry.RecordingId,
                        new HistoryEntry(
                            time.GetLocalNow(),
                            "recovered",
                            "info",
                            "Export interrupted",
                            $"Memento closed while exporting to {entry.OutputFolder ?? "the export folder"}. The {HumanFormat.Count(entry.Written.Count, "file", "files")} it had written there {(entry.Written.Count == 1 ? "was" : "were")} removed; nothing inside Memento was changed. Export again from Review."),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
                {
                    LogNotCleaned(ex, entry.RecordingId);
                }
            }

            Quietly(() => journal.Remove(entry.JobId), entry.JobId);
        }

        return entries.Count;
    }

    private void Quietly(Action action, string what)
    {
        try
        {
            action();
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogNotCleaned(ex, what);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Export {JobId} of recording {RecordingId} was cut short when Memento closed; its {Files} written files were removed")]
    private partial void LogCleaned(string jobId, string recordingId, int files);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clean-up of an interrupted export could not remove {Path}")]
    private partial void LogNotCleaned(Exception exception, string path);
}
