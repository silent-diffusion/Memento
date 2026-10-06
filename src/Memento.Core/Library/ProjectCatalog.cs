using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Library;

/// <summary>
/// The one way to change a project's manifest from the app: write it, refresh its index row, and tell the UI
/// with <c>library.changed</c>. Keeps the folder, the index and the screen in step.
/// </summary>
public sealed class ProjectCatalog(IProjectStore store, ILibraryIndex index, BridgeEventPublisher publisher)
{
    public async Task<ProjectManifest> SaveAsync(ProjectManifest manifest, CancellationToken cancellationToken)
    {
        var saved = await store.SaveAsync(manifest, cancellationToken);
        await index.UpsertAsync(saved, cancellationToken);
        NotifyChanged(saved.Id);
        return saved;
    }

    public async Task<ProjectManifest> UpdateAsync(string recordingId, Func<ProjectManifest, ProjectManifest> update, CancellationToken cancellationToken)
    {
        var saved = await store.UpdateAsync(recordingId, update, cancellationToken);
        await index.UpsertAsync(saved, cancellationToken);
        NotifyChanged(saved.Id);
        return saved;
    }

    /// <summary>
    /// After a write outside the manifest (annotations, history, files): refresh the index row, whose size changed,
    /// and tell the UI.
    /// </summary>
    public async Task TouchedAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        await index.UpsertAsync(manifest, cancellationToken);
        NotifyChanged(recordingId);
    }

    /// <summary>Only tells the UI (for example after a delete, when there is nothing left to index).</summary>
    public void NotifyChanged(params string[] recordingIds) =>
        publisher.PublishLibraryChanged(new LibraryChangedPayload(recordingIds));
}
