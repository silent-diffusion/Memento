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

    /// <summary>At most this many speakers in one <c>transcript.restoreSpeakers</c>.</summary>
    public const int MaxRestores = 500;

    /// <summary>At most this many lines in one <c>transcript.setSegmentsSpeaker</c>.</summary>
    public const int MaxSelectedLines = 100_000;

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

                // An edit back to the original wording (Undo) leaves the line unedited again.
                var original = segment.Edited?.Original ?? segment.Text;
                edited = segment with
                {
                    Text = value,
                    Words = keepWords || segment.Words.Count > 0 ? WordAligner.Realign(value, segment.Start, segment.End) : [],
                    Confidence = 1,
                    Edited = string.Equals(value, original, StringComparison.Ordinal) ? null : new TranscriptEdit(time.GetLocalNow(), original),
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

    /// <summary>
    /// <c>transcript.setSegmentsSpeaker</c> (2.0, selection mode): several lines to one speaker (a new one with
    /// <paramref name="newSpeakerName"/>, or none with both <c>null</c>) in one write. Every line must exist, or nothing changes.
    /// </summary>
    public async Task<SegmentsSpeakersResult> SetSegmentsSpeakerAsync(string recordingId, IReadOnlyList<string> segmentIds, string? speakerId, string? newSpeakerName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segmentIds);
        if (segmentIds.Count is 0 or > MaxSelectedLines)
        {
            throw Invalid($"Choose 1 to {MaxSelectedLines} lines.");
        }

        var name = newSpeakerName is null ? null : ValidateSpeakerName(newSpeakerName);
        var wanted = segmentIds.ToHashSet(StringComparer.Ordinal);
        List<TranscriptSegment> changed = [];
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                foreach (var segmentId in wanted)
                {
                    FindSegment(t, segmentId);
                }

                var speakers = t.Speakers.ToList();
                string? target = null;
                if (name is not null)
                {
                    target = TranscriptSpeakers.IdFor(TranscriptSpeakers.NextNumber(speakers));
                    speakers.Add(new Speaker(target, name, Renamed: true, TranscriptSpeakers.ColorFor(speakers.Count), 0));
                }
                else if (speakerId is not null)
                {
                    target = FindSpeaker(t, speakerId).Id;
                }

                var segments = t.Segments.Select(s => wanted.Contains(s.Id) ? s with { Speaker = target, SpeakerConfidence = target is null ? null : 1.0 } : s).ToList();
                changed = segments.Where(s => wanted.Contains(s.Id)).ToList();
                return t with { Segments = segments, Speakers = TranscriptSpeakers.WithTalkTime(speakers, segments) };
            },
            cancellationToken);
        await HistoryAsync(recordingId, "Speaker changed", HumanFormat.Count(changed.Count, "line", "lines"), cancellationToken);
        return new SegmentsSpeakersResult(changed, saved!.Speakers);
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

    /// <summary>
    /// <c>transcript.restoreSpeaker</c> (Undo): puts the speaker back with its id, name, colour and renamed flag (added after
    /// the others when the transcript lacks it) and assigns the listed lines to it. Every line must exist, or nothing changes.
    /// </summary>
    public async Task<MergeSpeakersResult> RestoreSpeakerAsync(string recordingId, SpeakerRestore speaker, IReadOnlyList<string> segmentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(segmentIds);
        var restored = ValidateRestore(speaker);
        var id = restored.Id;
        var wanted = segmentIds.ToHashSet(StringComparer.Ordinal);
        var changed = 0;
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                foreach (var segmentId in wanted)
                {
                    FindSegment(t, segmentId);
                }

                var speakers = t.Speakers.Any(s => s.Id == id)
                    ? t.Speakers.Select(s => s.Id == id ? restored with { TalkTimeMs = s.TalkTimeMs } : s).ToList()
                    : [.. t.Speakers, restored];
                changed = t.Segments.Count(s => wanted.Contains(s.Id) && s.Speaker != id);
                var segments = t.Segments.Select(s => wanted.Contains(s.Id) && s.Speaker != id ? s with { Speaker = id, SpeakerConfidence = 1.0 } : s).ToList();
                return t with { Segments = segments, Speakers = TranscriptSpeakers.WithTalkTime(speakers, segments) };
            },
            cancellationToken);
        await HistoryAsync(recordingId, "Speaker restored", HumanFormat.Count(changed, "line moved", "lines moved"), cancellationToken);
        return new MergeSpeakersResult(saved!.Speakers, changed);
    }

    /// <summary>
    /// <c>transcript.restoreSpeakers</c>: <see cref="RestoreSpeakerAsync"/> for each entry in order, in one write (the Undo of
    /// <see cref="ReduceSpeakersAsync"/>). Every line must exist, or nothing changes.
    /// </summary>
    public async Task<MergeSpeakersResult> RestoreSpeakersAsync(string recordingId, IReadOnlyList<SpeakerLines> restores, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(restores);
        if (restores.Count is 0 or > MaxRestores)
        {
            throw Invalid($"Restore 1 to {MaxRestores} speakers at a time.");
        }

        var checkedRestores = restores
            .Select(r => r?.Speaker is null || r.SegmentIds is null
                ? throw Invalid("Each speaker to restore needs its speaker and its segmentIds.")
                : (Speaker: ValidateRestore(r.Speaker), Lines: r.SegmentIds.ToHashSet(StringComparer.Ordinal)))
            .ToList();
        var changed = 0;
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                foreach (var segmentId in checkedRestores.SelectMany(r => r.Lines))
                {
                    FindSegment(t, segmentId);
                }

                var speakers = t.Speakers.ToList();
                var segments = t.Segments.ToList();
                foreach (var (speaker, lines) in checkedRestores)
                {
                    var index = speakers.FindIndex(s => s.Id == speaker.Id);
                    if (index >= 0)
                    {
                        speakers[index] = speaker with { TalkTimeMs = speakers[index].TalkTimeMs };
                    }
                    else
                    {
                        speakers.Add(speaker);
                    }

                    for (var i = 0; i < segments.Count; i++)
                    {
                        if (lines.Contains(segments[i].Id) && segments[i].Speaker != speaker.Id)
                        {
                            segments[i] = segments[i] with { Speaker = speaker.Id, SpeakerConfidence = 1.0 };
                            changed++;
                        }
                    }
                }

                return t with { Segments = segments, Speakers = TranscriptSpeakers.WithTalkTime(speakers, segments) };
            },
            cancellationToken);
        await HistoryAsync(
            recordingId,
            "Speakers restored",
            string.Create(CultureInfo.InvariantCulture, $"{HumanFormat.Count(checkedRestores.Count, "speaker", "speakers")}, {HumanFormat.Count(changed, "line moved", "lines moved")}"),
            cancellationToken);
        return new MergeSpeakersResult(saved!.Speakers, changed);
    }

    /// <summary>
    /// <c>transcript.reduceSpeakers</c>: merges the speakers whose voices are most alike (<see cref="SpeakerReducer"/>) until
    /// <paramref name="count"/> are left, in one write. Named speakers are never merged with each other.
    /// </summary>
    public async Task<ReduceSpeakersResult> ReduceSpeakersAsync(string recordingId, int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > WhoSpoke.MaxCount)
        {
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"Reduce to 1 to {WhoSpoke.MaxCount} speakers; {count} is not possible."));
        }

        await LoadManifestAsync(recordingId, cancellationToken);
        var voices = await Transcripts.LoadVoicesAsync(recordingId, cancellationToken);
        SpeakerReducer.Result? reduced = null;
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                reduced = SpeakerReducer.Reduce(t.Segments, t.Speakers, voices, count);
                return reduced.Merges.Count == 0 ? null : t with { Segments = reduced.Segments, Speakers = reduced.Speakers };
            },
            cancellationToken);
        if (saved is null)
        {
            var current = await LoadAsync(recordingId, cancellationToken);
            return new ReduceSpeakersResult(current?.Speakers ?? [], [], 0, reduced?.Basis ?? SpeakerReducer.BasisTalkTime);
        }

        await HistoryAsync(
            recordingId,
            "Speakers reduced",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{HumanFormat.Count(saved.Speakers.Count, "speaker", "speakers")} left, {HumanFormat.Count(reduced!.Merges.Count, "speaker", "speakers")} merged by {(reduced.Basis == SpeakerReducer.BasisVoices ? "voice" : "talk time")}, {HumanFormat.Count(reduced.SegmentsChanged, "line moved", "lines moved")}"),
            cancellationToken);
        return new ReduceSpeakersResult(saved.Speakers, reduced.Merges, reduced.SegmentsChanged, reduced.Basis);
    }

    /// <summary><c>transcript.removeSpeaker</c> (Undo of adding one): removes a speaker no line is assigned to.</summary>
    public async Task<IReadOnlyList<Speaker>> RemoveSpeakerAsync(string recordingId, string speakerId, CancellationToken cancellationToken)
    {
        var saved = await WriteAsync(
            recordingId,
            TranscriptChangeReasons.Edited,
            t =>
            {
                var speaker = FindSpeaker(t, speakerId);
                var lines = t.Segments.Count(s => s.Speaker == speaker.Id);
                if (lines > 0)
                {
                    throw new BridgeException(
                        DomainErrorCodes.TranscriptSpeakerInUse,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{speaker.Name} still says {HumanFormat.Count(lines, "line", "lines")}, so the speaker was kept. Nothing was changed. Move those lines to another speaker first, or merge the speakers."),
                        speaker.Id);
                }

                return t with { Speakers = t.Speakers.Where(s => s.Id != speaker.Id).ToList() };
            },
            cancellationToken);
        await HistoryAsync(recordingId, "Speaker removed", null, cancellationToken);
        return saved!.Speakers;
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

    /// <summary>A speaker to put back (Undo): a valid id, a name of 1–100 characters and a colour 1–4; talk time is recomputed.</summary>
    private static Speaker ValidateRestore(SpeakerRestore speaker)
    {
        var id = ValidateSpeakerId(speaker.Id);
        var name = ValidateSpeakerName(speaker.Name);
        if (speaker.Color is < 1 or > TranscriptSpeakers.Colors)
        {
            throw Invalid($"A speaker colour is a number from 1 to {TranscriptSpeakers.Colors}; this one is {speaker.Color}.");
        }

        return new Speaker(id, name, speaker.Renamed, speaker.Color, 0);
    }

    /// <summary>A speaker id as the host writes them (<c>spk3</c>) or the mock does (<c>sp3</c>): letters, digits, '-' and '_'.</summary>
    private static string ValidateSpeakerId(string id)
    {
        var value = id ?? string.Empty;
        if (value.Length is 0 or > 40 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw Invalid("A speaker id is 1 to 40 letters, digits, '-' or '_'.");
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
