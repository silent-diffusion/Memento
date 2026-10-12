using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Library;

/// <summary>
/// The one way to change a project's manifest from the app: write it, refresh its index row, and tell the UI
/// with <c>library.changed</c>. Keeps the folder, the index and the screen in step.
/// </summary>
/// <remarks>
/// Each write and its index refresh run one at a time. A refresh reads the manifest and then writes the row; when a
/// manifest write landed between the two (a stage finishing while the user changed the recording, more likely the
/// slower the machine), the refresh put the older manifest back into the index, and the Library showed stages and
/// details the folder no longer had until the next change.
/// </remarks>
public sealed class ProjectCatalog(IProjectStore store, ILibraryIndex index, BridgeEventPublisher publisher) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<ProjectManifest> SaveAsync(ProjectManifest manifest, CancellationToken cancellationToken) =>
        InStepAsync(() => store.SaveAsync(manifest, cancellationToken), cancellationToken);

    public Task<ProjectManifest> UpdateAsync(string recordingId, Func<ProjectManifest, ProjectManifest> update, CancellationToken cancellationToken) =>
        InStepAsync(() => store.UpdateAsync(recordingId, update, cancellationToken), cancellationToken);

    /// <summary>
    /// After a write outside the manifest (annotations, history, files): refresh the index row, whose size changed,
    /// and tell the UI.
    /// </summary>
    public Task TouchedAsync(string recordingId, CancellationToken cancellationToken) =>
        InStepAsync(() => store.LoadAsync(recordingId, cancellationToken), cancellationToken);

    public void Dispose() => _gate.Dispose();

    /// <summary>Only tells the UI (for example after a delete, when there is nothing left to index).</summary>
    public void NotifyChanged(params string[] recordingIds) =>
        publisher.PublishLibraryChanged(new LibraryChangedPayload(recordingIds));

    /// <summary>Reads or writes the manifest and indexes exactly that version, with no other catalog write in between.</summary>
    private async Task<ProjectManifest> InStepAsync(Func<Task<ProjectManifest>> manifest, CancellationToken cancellationToken)
    {
        ProjectManifest current;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            current = await manifest();
            await index.UpsertAsync(current, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        NotifyChanged(current.Id);
        return current;
    }
}
