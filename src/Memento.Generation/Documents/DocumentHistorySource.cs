using Memento.Core.Documents;
using Memento.Core.History;

namespace Memento.Generation.Documents;

/// <summary>
/// Core's <see cref="IDocumentHistorySource"/>: every document's current content and, while version history is on, its
/// kept versions, each with the time its content came about and the time it was replaced.
/// </summary>
public sealed class DocumentHistorySource(ProjectDocumentStore store) : IDocumentHistorySource
{
    public async Task<IReadOnlyList<HistorySnapshot>> SnapshotsAsync(string recordingId, bool keptVersions, CancellationToken cancellationToken)
    {
        var snapshots = new List<HistorySnapshot>();
        foreach (var document in await store.ListAsync(recordingId, cancellationToken))
        {
            snapshots.Add(new HistorySnapshot(HistoryKinds.Document, document.Id, HistorySnapshot.CurrentId, document.LastChange?.At ?? document.ModifiedAt, null));
            if (!keptVersions)
            {
                continue;
            }

            foreach (var version in await store.ListVersionsAsync(recordingId, document.Id, cancellationToken))
            {
                snapshots.Add(new HistorySnapshot(
                    HistoryKinds.Document,
                    document.Id,
                    version.Id,
                    version.Document.LastChange?.At ?? version.SavedAt,
                    HistorySnapshot.StampOf(version.Id)));
            }
        }

        return snapshots;
    }
}
