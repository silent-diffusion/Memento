using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Library;

/// <summary>
/// The SQLite index over the project folders (<c>library.db</c>). It is a cache: the project folders are the
/// truth, and <see cref="RebuildAsync"/> recreates it from them at any time.
/// </summary>
public interface ILibraryIndex
{
    /// <summary>
    /// Opens or creates the database. A missing database is built from the project folders; a corrupt one is set
    /// aside with a timestamped name and rebuilt. Called at start-up; every other member calls it on first use.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    Task UpsertAsync(ProjectManifest manifest, CancellationToken cancellationToken);

    Task RemoveAsync(string recordingId, CancellationToken cancellationToken);

    /// <summary>The Library list: every state except <c>recording</c> (BRIDGE.md), filtered and sorted.</summary>
    Task<LibraryQueryResult> QueryAsync(LibraryQuery query, CancellationToken cancellationToken);

    /// <summary>Recordings with a stage active or queued, newest first, with every stage (including <c>stored</c>).</summary>
    Task<IReadOnlyList<ProcessingEntry>> ListProcessingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListIdsByStateAsync(string state, CancellationToken cancellationToken);

    /// <summary>Empties the index and re-reads every project folder; returns how many recordings were indexed.</summary>
    Task<int> RebuildAsync(CancellationToken cancellationToken);
}
