using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.History;

/// <summary>
/// Which History lines can open a stored copy (<c>history.links</c>, BRIDGE.md "History links"). A line that changed
/// the transcript or a document belongs to the copy that was current at its time: a copy holds the content from the
/// write that defined it (<see cref="HistorySnapshot.Start"/>) until a write replaced it (<see cref="HistorySnapshot.End"/>),
/// and the line is written just after its write. Of the lines inside one copy only the last opens it, because the
/// earlier ones were changed again before a copy was kept (the transcript as it was before speakers were identified,
/// the first of a run of edits); those, and lines whose copy was removed after the version-history days, have no
/// version. Lines that changed neither (recorded, stored, exported, details, failures) are not listed.
/// </summary>
public static class HistoryLinker
{
    /// <summary>How long after a document's write its History line may come (generation exports copies in between).</summary>
    public static readonly TimeSpan DocumentLineWindow = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<HistoryLink> Link(IReadOnlyList<HistoryEntry> entries, IReadOnlyList<HistorySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(snapshots);
        var found = new List<(int Index, string Kind, HistorySnapshot? Copy, string After)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (Describe(entry) is not { } change)
            {
                continue;
            }

            var copy = snapshots
                .Where(s => s.Kind == change.Kind
                    && (s.Start is not { } start || start <= entry.At)
                    && (s.End is not { } end || entry.At < end))
                .MaxBy(s => s.Start ?? DateTimeOffset.MinValue);
            if (copy is not null && change.Kind == HistoryKinds.Document && copy.Start is { } written && entry.At - written > DocumentLineWindow)
            {
                copy = null;
            }

            found.Add((i, change.Kind, copy, change.After));
        }

        // Only the last line inside a copy opens it.
        var last = new Dictionary<HistorySnapshot, int>();
        foreach (var (index, _, copy, _) in found)
        {
            if (copy is not null)
            {
                last[copy] = index;
            }
        }

        return found
            .Select(f =>
            {
                var opens = f.Copy is not null && last[f.Copy] == f.Index;
                return new HistoryLink(f.Index, f.Kind, f.Copy?.DocumentId, opens ? f.Copy!.VersionId : null, f.After);
            })
            .ToList();
    }

    /// <summary>What a line changed and the banner's words for the content right after it, or <c>null</c>.</summary>
    public static (string Kind, string After)? Describe(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var summary = entry.Summary ?? string.Empty;
        switch (entry.Stage)
        {
            case StageNames.Transcript when entry.Event == "completed":
                return (HistoryKinds.Transcript, "after it was transcribed");
            case StageNames.Speakers when entry.Event == "completed":
                return (HistoryKinds.Transcript, "after speakers were identified");
            case StageNames.Minutes when entry.Event == "completed":
                return (HistoryKinds.Document, summary.Contains(" regenerated ", StringComparison.Ordinal) ? "after it was generated again" : "after it was generated");
            case "edited" when entry.Event == "info":
                return Edited(summary);
            default:
                return null;
        }
    }

    // The summaries TranscriptService and DocumentService write (BRIDGE.md, History).
    private static (string Kind, string After)? Edited(string summary) => summary switch
    {
        "Transcript edited" => (HistoryKinds.Transcript, "after a line was edited"),
        "Speaker changed" => (HistoryKinds.Transcript, "after a line's speaker was changed"),
        "Speaker renamed" => (HistoryKinds.Transcript, "after a speaker was renamed"),
        "Speakers merged" => (HistoryKinds.Transcript, "after speakers were merged"),
        "Speaker restored" or "Speaker removed" => (HistoryKinds.Transcript, "after a speaker change was undone"),
        "Transcript version restored" => (HistoryKinds.Transcript, "after an earlier version was restored"),
        _ when summary.StartsWith("Speaker", StringComparison.Ordinal) => (HistoryKinds.Transcript, "after speakers were changed"),
        _ when summary.StartsWith("Document \"", StringComparison.Ordinal) && summary.EndsWith("\" created as a copy", StringComparison.Ordinal) => (HistoryKinds.Document, "after it was copied"),
        _ when summary.StartsWith("Document \"", StringComparison.Ordinal) && summary.EndsWith("\" created", StringComparison.Ordinal) => (HistoryKinds.Document, "after it was created"),
        _ when summary.StartsWith("Document \"", StringComparison.Ordinal) && summary.EndsWith("\" edited", StringComparison.Ordinal) => (HistoryKinds.Document, "after it was edited"),
        _ when summary.StartsWith("Document \"", StringComparison.Ordinal) && summary.EndsWith("\" restored", StringComparison.Ordinal) => (HistoryKinds.Document, "after an earlier version was restored"),
        _ => null,
    };
}
