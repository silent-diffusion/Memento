using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Settings;

namespace Memento.Core.Transcripts;

/// <summary>
/// The one way to write a transcript: through <see cref="TranscriptStore"/> (atomic, versioned), then refresh the
/// library's full-text index and the project row, and raise <c>transcript.changed</c>.
/// </summary>
public sealed class TranscriptWriter(
    TranscriptStore transcripts,
    ILibraryIndex index,
    ProjectCatalog catalog,
    BridgeEventPublisher publisher,
    ISettingsStore settings)
{
    public TranscriptStore Store => transcripts;

    /// <summary>
    /// Loads, changes and writes the transcript (see <see cref="TranscriptStore.UpdateAsync"/>); returns what was
    /// written, or <c>null</c> when <paramref name="update"/> wrote nothing.
    /// </summary>
    public async Task<TranscriptDocument?> UpdateAsync(
        string recordingId,
        string reason,
        Func<TranscriptDocument?, TranscriptDocument?> update,
        CancellationToken cancellationToken)
    {
        var saved = await transcripts.UpdateAsync(recordingId, reason, settings.Current.History, update, cancellationToken);
        if (saved is null)
        {
            return null;
        }

        await index.SetTranscriptAsync(recordingId, saved.IndexText(), saved.RenamedSpeakers(), cancellationToken);
        await catalog.TouchedAsync(recordingId, cancellationToken);
        var eventReason = reason == TranscriptChangeReasons.Retranscribed ? TranscriptChangeReasons.Transcribed : reason;
        publisher.PublishTranscriptChanged(new TranscriptChangedPayload(recordingId, saved.Version, eventReason));
        return saved;
    }

    /// <summary>Tells the UI the topics changed (they live in <c>annotations.json</c>, beside the transcript).</summary>
    public void PublishTopicsChanged(string recordingId, int version) =>
        publisher.PublishTranscriptChanged(new TranscriptChangedPayload(recordingId, version, TranscriptChangeReasons.Topics));
}
