using System.Globalization;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Formatting;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Transcripts;

/// <summary>
/// The <c>transcript.*</c> bridge methods. Every write goes through <see cref="TranscriptWriter"/> (version counter,
/// kept versions, index, <c>transcript.changed</c>) and appends a History line.
/// </summary>
public sealed partial class TranscriptService(
    TranscriptWriter writer,
    IProjectStore store,
    ProcessingOrchestrator processing,
    RecordingCoordinator recordings,
    ProcessingGate gate,
    IModelManager models,
    ISettingsStore settings,
    TimeProvider time,
    ILogger<TranscriptService> logger)
{
    public const int MaxSegmentTextLength = 4000;
    public const int MaxSpeakerNameLength = 100;

    private readonly ILogger<TranscriptService> _logger = logger;

    private TranscriptStore Transcripts => writer.Store;

    public async Task<TranscriptGetResult> GetAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await LoadManifestAsync(recordingId, cancellationToken);
        var transcript = await LoadAsync(recordingId, cancellationToken);
        var stage = StageList.Find(manifest.Stages, StageNames.Transcript);
        var status = stage?.State switch
        {
            null => transcript is null ? TranscriptStatuses.None : TranscriptStatuses.Done,
            StageStates.Done => TranscriptStatuses.Done,
            StageStates.Failed => TranscriptStatuses.Failed,
            StageStates.Queued when gate.IsPaused || (stage.Label?.StartsWith("Paused", StringComparison.Ordinal) ?? false) => TranscriptStatuses.Paused,
            StageStates.Queued => TranscriptStatuses.Queued,
            _ => gate.IsPaused || (stage.Label?.StartsWith("Paused", StringComparison.Ordinal) ?? false) ? TranscriptStatuses.Paused : TranscriptStatuses.Running,
        };
        var failure = manifest.Failures.FirstOrDefault(f => f.Stage == StageNames.Transcript)
            ?? manifest.Failures.FirstOrDefault(f => f.Stage == StageNames.Speakers);
        return new TranscriptGetResult(transcript?.ToContract(), status, failure?.ToContract());
    }

    public async Task<TranscriptSegmentResult> EditSegmentAsync(string recordingId, string segmentId, string text, CancellationToken cancellationToken)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length > MaxSegmentTextLength)
        {
            throw Invalid($"A segment can hold at most {MaxSegmentTextLength} characters; this one has {value.Length}.");
        }

        TranscriptSegment? edited = null;
        var keepWords = settings.Current.Transcription.KeepWordTimestamps;
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                var segment = FindSegment(t, segmentId);
                if (segment.Text == value)
                {
                    edited = segment;
                    return null;
                }

                edited = segment with
                {
                    Text = value,
                    Words = keepWords || segment.Words.Count > 0 ? WordAligner.Realign(value, segment.Start, segment.End) : [],
                    Confidence = 1,
                    Edited = new TranscriptEdit(time.GetLocalNow(), segment.Edited?.Original ?? segment.Text),
                };
                return t with { Segments = Replace(t.Segments, edited) };
            },
            cancellationToken);
        if (saved is null)
        {
            return new TranscriptSegmentResult(edited!, (await LoadAsync(recordingId, cancellationToken))!.Version);
        }

        await HistoryAsync(recordingId, "Transcript edited", string.Create(CultureInfo.InvariantCulture, $"Line at {HumanFormat.Clock((long)(edited!.Start * 1000))}"), cancellationToken);
        return new TranscriptSegmentResult(edited, saved.Version);
    }

    public async Task<SegmentSpeakersResult> SetSegmentSpeakerAsync(string recordingId, string segmentId, string? speakerId, string? newSpeakerName, CancellationToken cancellationToken)
    {
        var name = newSpeakerName is null ? null : ValidateSpeakerName(newSpeakerName);
        TranscriptSegment? changed = null;
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                var segment = FindSegment(t, segmentId);
                var speakers = t.Speakers.ToList();
                string? target = null;
                if (name is not null)
                {
                    var number = TranscriptSpeakers.NextNumber(speakers);
                    target = TranscriptSpeakers.IdFor(number);
                    speakers.Add(new Speaker(target, name, Renamed: true, TranscriptSpeakers.ColorFor(speakers.Count), 0));
                }
                else if (speakerId is not null)
                {
                    target = FindSpeaker(t, speakerId).Id;
                }

                changed = segment with { Speaker = target, SpeakerConfidence = target is null ? null : 1.0 };
                var segments = Replace(t.Segments, changed);
                return t with { Segments = segments, Speakers = TranscriptSpeakers.WithTalkTime(speakers, segments) };
            },
            cancellationToken);
        await HistoryAsync(recordingId, "Speaker changed", string.Create(CultureInfo.InvariantCulture, $"Line at {HumanFormat.Clock((long)(changed!.Start * 1000))}"), cancellationToken);
        return new SegmentSpeakersResult(changed, saved!.Speakers);
    }

    public async Task<IReadOnlyList<Speaker>> RenameSpeakerAsync(string recordingId, string speakerId, string name, CancellationToken cancellationToken)
    {
        var value = ValidateSpeakerName(name);
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                var speaker = FindSpeaker(t, speakerId);
                return t with { Speakers = t.Speakers.Select(s => s.Id == speaker.Id ? s with { Name = value, Renamed = true } : s).ToList() };
            },
            cancellationToken);
        await HistoryAsync(recordingId, "Speaker renamed", null, cancellationToken);
        return saved!.Speakers;
    }

    public async Task<MergeSpeakersResult> MergeSpeakersAsync(string recordingId, string fromSpeakerId, string intoSpeakerId, CancellationToken cancellationToken)
    {
        if (string.Equals(fromSpeakerId, intoSpeakerId, StringComparison.Ordinal))
        {
            throw Invalid("Choose two different speakers to merge.");
        }

        var changed = 0;
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                var from = FindSpeaker(t, fromSpeakerId);
                var into = FindSpeaker(t, intoSpeakerId);
                changed = t.Segments.Count(s => s.Speaker == from.Id);
                var segments = t.Segments.Select(s => s.Speaker == from.Id ? s with { Speaker = into.Id } : s).ToList();
                var speakers = t.Speakers.Where(s => s.Id != from.Id).ToList();
                return t with { Segments = segments, Speakers = TranscriptSpeakers.WithTalkTime(speakers, segments) };
            },
            cancellationToken);
        await HistoryAsync(recordingId, "Speakers merged", HumanFormat.Count(changed, "line moved", "lines moved"), cancellationToken);
        return new MergeSpeakersResult(saved!.Speakers, changed);
    }

    public async Task<bool> MarkReviewedAsync(string recordingId, bool reviewed, CancellationToken cancellationToken)
    {
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t => t.Reviewed == reviewed ? null : t with { Reviewed = reviewed },
            cancellationToken);
        if (saved is not null)
        {
            await HistoryAsync(recordingId, reviewed ? "Marked as reviewed" : "Marked as not reviewed", null, cancellationToken);
        }

        return reviewed;
    }

    public async Task<IReadOnlyList<TranscriptMatch>> SearchAsync(string recordingId, string query, CancellationToken cancellationToken)
    {
        if (query is { Length: > TranscriptSearch.MaxQueryLength })
        {
            throw Invalid($"A search can be at most {TranscriptSearch.MaxQueryLength} characters.");
        }

        await LoadManifestAsync(recordingId, cancellationToken);
        var transcript = await LoadAsync(recordingId, cancellationToken) ?? throw NoTranscript(recordingId);
        return TranscriptSearch.Find(transcript.Segments, query ?? string.Empty);
    }

    /// <summary><c>transcript.retranscribe</c>: queues a new pass with another model or language.</summary>
    public async Task RetranscribeAsync(string recordingId, string? modelId, string? language, CancellationToken cancellationToken)
    {
        var manifest = await LoadManifestAsync(recordingId, cancellationToken);
        if (recordings.IsBusy(recordingId) || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
        {
            throw new BridgeException(
                DomainErrorCodes.ProjectRecording,
                $"\"{manifest.Details.Title}\" is still recording or being saved, so it can't be transcribed again yet. Wait until it is saved, then try again.");
        }

        if (modelId is not null)
        {
            var entry = models.Catalog.Find(modelId);
            if (entry is not { Kind: ModelKinds.Transcription })
            {
                throw new BridgeException(DomainErrorCodes.ModelsNotFound, $"There is no transcription model called '{modelId}'. Nothing was changed. Choose one of the models in Settings › Transcription.", modelId);
            }

            if (!models.IsInstalled(modelId))
            {
                throw new BridgeException(
                    DomainErrorCodes.EngineUnavailable,
                    $"{entry.Name} is not installed, so the recording can't be transcribed with it. Nothing was changed. Install it in Settings › Transcription, then try again.",
                    $"Install {entry.Name} ({HumanFormat.Bytes(entry.SizeBytes)}) in Settings › Transcription.");
            }
        }

        if (language is not null && !TranscriptionSettings.IsValidLanguage(language))
        {
            throw Invalid($"Language '{language}' is not available. Choose auto or a two- or three-letter language code such as en.");
        }

        // The new pass starts from scratch: stop a running one and drop its partial results first.
        await processing.StopForRequeueAsync(recordingId, StageNames.Transcript);
        Transcripts.DeletePartial(recordingId);
        await processing.RetranscribeAsync(recordingId, new ProcessingRequest(modelId, language), cancellationToken);
        await HistoryAsync(
            recordingId,
            "Transcription queued again",
            string.Join(" · ", new[] { modelId is null ? null : "model " + modelId, language is null ? null : "language " + language }.Where(s => s is not null)) is { Length: > 0 } detail ? detail : null,
            cancellationToken,
            StageNames.Transcript);
    }

    public async Task<IReadOnlyList<TranscriptVersion>> VersionsAsync(string recordingId, CancellationToken cancellationToken)
    {
        await LoadManifestAsync(recordingId, cancellationToken);
        if (!settings.Current.History.KeepVersions)
        {
            return [];
        }

        var versions = await Transcripts.ListVersionsAsync(recordingId, cancellationToken);
        return versions
            .Select(v => new TranscriptVersion(
                v.Id,
                v.SavedAt,
                v.Reason,
                string.IsNullOrEmpty(v.Transcript.Engine.Model) ? null : $"{v.Transcript.Engine.Name} {v.Transcript.Engine.Model}",
                v.Transcript.Segments.Count))
            .ToList();
    }

    public async Task<Transcript> RestoreVersionAsync(string recordingId, string versionId, CancellationToken cancellationToken)
    {
        await LoadManifestAsync(recordingId, cancellationToken);
        var version = await Transcripts.LoadVersionAsync(recordingId, versionId, cancellationToken)
            ?? throw new BridgeException(
                DomainErrorCodes.TranscriptVersionNotFound,
                "That version of the transcript is no longer kept; versions are removed after the number of days set in Settings › History. Nothing was changed.",
                versionId);
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Restored,
            current => version.Transcript with { Version = current?.Version ?? version.Transcript.Version, Complete = true },
            cancellationToken,
            allowMissing: true);
        await HistoryAsync(recordingId, "Transcript version restored", $"Version from {version.SavedAt.ToString("g", CultureInfo.CurrentCulture)}", cancellationToken);
        return saved!.ToContract();
    }

    private static BridgeException Invalid(string message) => new(BridgeErrorCodes.InvalidParams, message);

    private static BridgeException NoTranscript(string recordingId) =>
        new(
            DomainErrorCodes.TranscriptNone,
            "This recording has no transcript yet. Nothing was changed. It appears when transcription finishes; start it from the recording's processing card.",
            recordingId);

    private static TranscriptSegment FindSegment(TranscriptDocument transcript, string segmentId) =>
        transcript.Segments.FirstOrDefault(s => s.Id == segmentId)
        ?? throw new BridgeException(
            DomainErrorCodes.TranscriptSegmentNotFound,
            "That line is not in the transcript any more; it may have been transcribed again. Nothing was changed. Reopen the transcript to see the current lines.",
            segmentId);

    private static Speaker FindSpeaker(TranscriptDocument transcript, string speakerId) =>
        transcript.Speakers.FirstOrDefault(s => s.Id == speakerId)
        ?? throw new BridgeException(
            DomainErrorCodes.TranscriptSpeakerNotFound,
            "That speaker is not in this transcript any more; speakers may have been merged or identified again. Nothing was changed.",
            speakerId);

    private static List<TranscriptSegment> Replace(IReadOnlyList<TranscriptSegment> segments, TranscriptSegment changed) =>
        segments.Select(s => s.Id == changed.Id ? changed : s).ToList();

    private static string ValidateSpeakerName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxSpeakerNameLength)
        {
            throw Invalid($"A speaker name needs 1 to {MaxSpeakerNameLength} characters.");
        }

        return value;
    }

    private async Task<ProjectManifest> LoadManifestAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }
    }

    private async Task<TranscriptDocument?> LoadAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await Transcripts.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, ex.Message);
        }
    }

    private async Task<TranscriptDocument?> WriteAsync(
        string recordingId,
        string reason,
        Func<TranscriptDocument, TranscriptDocument?> update,
        CancellationToken cancellationToken,
        bool allowMissing = false)
    {
        await LoadManifestAsync(recordingId, cancellationToken);
        try
        {
            return await writer.UpdateAsync(
                recordingId,
                reason,
                current => current is null && !allowMissing ? throw NoTranscript(recordingId) : update(current!),
                cancellationToken);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, ex.Message);
        }
    }

    private async Task HistoryAsync(string recordingId, string summary, string? detail, CancellationToken cancellationToken, string stage = "edited")
    {
        try
        {
            await store.AppendHistoryAsync(
                recordingId,
                new HistoryEntry(time.GetLocalNow(), stage, "info", summary, detail),
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "History of recording {RecordingId} could not be appended")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);
}
