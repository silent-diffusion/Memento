using Memento.Core.History;

namespace Memento.Core.Documents;

/// <summary>
/// The stored copies of a recording's documents (each document's current content and its kept versions), for
/// <c>history.links</c>. Implemented with the M4 document store (Memento.Generation).
/// </summary>
public interface IDocumentHistorySource
{
    /// <param name="keptVersions">Whether kept versions are listed (Settings › Documents › version history is on).</param>
    Task<IReadOnlyList<HistorySnapshot>> SnapshotsAsync(string recordingId, bool keptVersions, CancellationToken cancellationToken);
}
