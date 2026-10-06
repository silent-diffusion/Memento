using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>
/// One folder per recording under <c>&lt;library&gt;\projects</c> (ARCHITECTURE.md §4). Every JSON write is atomic
/// (<c>.tmp</c>, flush, move) and carries a schema version; writes to one project are serialized.
/// Ids are validated with <see cref="ProjectId.IsValid"/> before they touch the file system.
/// </summary>
public interface IProjectStore
{
    /// <summary><c>&lt;library&gt;\projects</c>.</summary>
    string ProjectsRoot { get; }

    /// <exception cref="ProjectNotFoundException">The id is not a valid project id.</exception>
    string GetProjectFolder(string recordingId);

    bool Exists(string recordingId);

    /// <summary>Ids of every project folder that has a <c>project.json</c>, in no particular order.</summary>
    IReadOnlyList<string> ListIds();

    /// <summary>Creates the folder layout and the first manifest.</summary>
    Task<ProjectManifest> CreateAsync(ProjectCreateRequest request, CancellationToken cancellationToken);

    /// <exception cref="ProjectNotFoundException">No such project or its manifest is unreadable.</exception>
    Task<ProjectManifest> LoadAsync(string recordingId, CancellationToken cancellationToken);

    /// <summary>Writes the manifest with the current schema version and a new <see cref="ProjectManifest.ModifiedAt"/>.</summary>
    /// <exception cref="ProjectSchemaException">The manifest came from a newer Memento.</exception>
    Task<ProjectManifest> SaveAsync(ProjectManifest manifest, CancellationToken cancellationToken);

    /// <summary>Load, change and save under the project's write lock.</summary>
    Task<ProjectManifest> UpdateAsync(string recordingId, Func<ProjectManifest, ProjectManifest> update, CancellationToken cancellationToken);

    /// <summary>Deletes the whole project folder. Only the designed Delete flow calls this.</summary>
    /// <exception cref="ProjectBusyException">The project has a <c>recording.state.json</c> (recording or awaiting recovery).</exception>
    Task DeleteAsync(string recordingId, CancellationToken cancellationToken);

    /// <summary>Total size of every file in the project folder.</summary>
    long GetSizeBytes(string recordingId);

    /// <summary>Appends one line to <c>history.jsonl</c> and flushes it to disk.</summary>
    Task AppendHistoryAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken);

    /// <summary>Every readable history line, oldest first; damaged lines are skipped.</summary>
    Task<IReadOnlyList<HistoryEntry>> ReadHistoryAsync(string recordingId, CancellationToken cancellationToken);

    Task<AnnotationsDocument> LoadAnnotationsAsync(string recordingId, CancellationToken cancellationToken);

    Task<AnnotationsDocument> UpdateAnnotationsAsync(string recordingId, Func<AnnotationsDocument, AnnotationsDocument> update, CancellationToken cancellationToken);

    Task WriteRecordingStateAsync(RecordingStateDocument state, CancellationToken cancellationToken);

    /// <summary>The recording state, or <c>null</c> when there is none or it is unreadable.</summary>
    Task<RecordingStateDocument?> ReadRecordingStateAsync(string recordingId, CancellationToken cancellationToken);

    bool HasRecordingState(string recordingId);

    void DeleteRecordingState(string recordingId);
}
