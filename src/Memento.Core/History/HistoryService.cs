using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Documents;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Transcripts;

namespace Memento.Core.History;

/// <summary>
/// <c>history.links</c> and <c>transcript.getVersion</c>: which History lines open a stored copy of the transcript or a
/// document (<see cref="HistoryLinker"/>), and a kept transcript version to read. Kept versions are listed only while
/// Settings › Documents › version history is on, as <c>transcript.versions</c> does; retention is unchanged.
/// </summary>
public sealed class HistoryService(IProjectStore store, TranscriptStore transcripts, ISettingsStore settings, IDocumentHistorySource? documents = null)
{
    public async Task<IReadOnlyList<HistoryLink>> LinksAsync(string recordingId, CancellationToken cancellationToken)
    {
        await RequireProjectAsync(recordingId, cancellationToken);
        var entries = await store.ReadHistoryAsync(recordingId, cancellationToken);
        var kept = settings.Current.History.KeepVersions;
        var snapshots = new List<HistorySnapshot>();
        if (await LoadTranscriptAsync(recordingId, cancellationToken) is { } current)
        {
            snapshots.Add(new HistorySnapshot(HistoryKinds.Transcript, null, HistorySnapshot.CurrentId, current.LastChange?.At, null));
        }

        if (kept)
        {
            foreach (var version in await transcripts.ListVersionsAsync(recordingId, cancellationToken))
            {
                snapshots.Add(new HistorySnapshot(
                    HistoryKinds.Transcript,
                    null,
                    version.Id,
                    version.Transcript.LastChange?.At ?? version.SavedAt,
                    HistorySnapshot.StampOf(version.Id)));
            }
        }

        if (documents is not null)
        {
            snapshots.AddRange(await documents.SnapshotsAsync(recordingId, kept, cancellationToken));
        }

        return HistoryLinker.Link(entries, snapshots);
    }

    /// <summary>A kept transcript version, whole, to read.</summary>
    /// <exception cref="BridgeException"><c>transcript.versionNotFound</c> when it is not kept (any more).</exception>
    public async Task<Transcript> GetTranscriptVersionAsync(string recordingId, string versionId, CancellationToken cancellationToken)
    {
        await RequireProjectAsync(recordingId, cancellationToken);
        var version = await transcripts.LoadVersionAsync(recordingId, versionId, cancellationToken)
            ?? throw new BridgeException(
                DomainErrorCodes.TranscriptVersionNotFound,
                "That version of the transcript is no longer kept; versions are removed after the number of days set in Settings › Documents › version history. Nothing was changed.",
                versionId);
        return version.Transcript.ToContract();
    }

    private async Task RequireProjectAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            await store.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }
    }

    private async Task<TranscriptDocument?> LoadTranscriptAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await transcripts.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, ex.Message);
        }
    }
}
