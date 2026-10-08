using System.Diagnostics;
using System.Globalization;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Memento.Transcription.Speakers;
using Microsoft.Extensions.Logging;

namespace Memento.Transcription.Stages;

/// <summary>
/// The <c>speakers</c> stage: sherpa-onnx offline diarization of each track that has transcript lines, in the worker
/// (pyannote segmentation 3.0 + the voice model from Settings, clustering by threshold, 4 threads), then speakers are
/// assigned to segments by time overlap within each track and the voices of all tracks are grouped into people
/// (<see cref="SpeakerAssigner"/>): to the recording's own count (Who spoke) or Settings' expected speakers when there is
/// one, for any number of tracks, else by how alike they sound. Names a person gave before carry over, and the
/// recording's known names go to the others (<see cref="SpeakerNamer"/>). The diarizer's output is kept in
/// <c>voices.json</c>, so identifying the speakers again with another count or other names only regroups.
/// </summary>
public sealed partial class SpeakersStage(
    IProjectStore store,
    TranscriptWriter writer,
    StageStatusWriter status,
    IModelManager models,
    WorkerClient workers,
    ISettingsStore settings,
    TimeProvider time,
    ILogger<SpeakersStage> logger) : IProcessingStage
{
    private readonly StageHistory _history = new(store, time, logger);
    private readonly ILogger<SpeakersStage> _logger = logger;

    public string Name => StageNames.Speakers;

    public int Order => 20;

    public bool IsHeavy => true;

    public bool AppliesTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Transcription.Auto && settings.Speakers.Identify;
    }

    public async Task RunAsync(StageRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var recordingId = run.RecordingId;
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        var transcript = await writer.Store.LoadAsync(recordingId, cancellationToken);
        if (transcript is null || transcript.Segments.Count == 0)
        {
            await status.RemoveAsync(recordingId, Name, cancellationToken);
            if (transcript is not null)
            {
                await _history.AppendAsync(recordingId, Name, "info", "No speakers to identify", "The transcript has no lines.");
            }

            return;
        }

        var current = settings.Current.Speakers;
        var segmentation = models.Catalog.Entries.FirstOrDefault(e => e.Kind == ModelKinds.Speakers && e.Role == ModelRoles.Segmentation);
        var embedding = models.Catalog.Find(current.EmbeddingModelId);

        // Checked against their SHA-256 before use (hashed only when a stamp does not vouch for them), so a damaged file
        // is named as damaged rather than as not installed.
        var damaged = new List<ModelCatalogEntry>();
        foreach (var model in new[] { segmentation, embedding }.OfType<ModelCatalogEntry>())
        {
            if (await models.VerifyAsync(model.Id, cancellationToken) == ModelCheck.Damaged)
            {
                damaged.Add(model);
            }
        }

        if (damaged.Count > 0)
        {
            var names = string.Join(" and ", damaged.Select(d => d.Name));
            await FailAsync(
                recordingId,
                $"Speaker identification could not start: the installed {names} {(damaged.Count == 1 ? "file is" : "files are")} damaged (the SHA-256 checksum does not match the published one), so Memento set {(damaged.Count == 1 ? "it" : "them")} aside instead of using {(damaged.Count == 1 ? "it" : "them")}.",
                "The transcript is kept without speakers. Download the speaker models again and speakers are identified by themselves.",
                damaged.Select(d => new Remedy(Remedies.Install(d.Id), $"Download {d.Name} again")).ToList(),
                ProjectStageFailure.CauseNoModel,
                "Waiting for a model",
                cancellationToken);
            return;
        }

        var segmentationPath = segmentation is null ? null : models.Resolve(segmentation.Id);
        var embeddingPath = embedding is null ? null : models.Resolve(embedding.Id);
        if (segmentationPath is null || embeddingPath is null)
        {
            var missing = new[] { segmentationPath is null ? segmentation?.Name : null, embeddingPath is null ? embedding?.Name ?? current.EmbeddingModelId : null }
                .OfType<string>();
            await FailAsync(
                recordingId,
                $"Speaker identification needs {string.Join(" and ", missing)}, and {(missing.Count() == 1 ? "it is" : "they are")} not installed.",
                "The transcript is kept without speakers. Install the speaker models in Settings › Speakers and speakers are identified by themselves.",
                [new Remedy(Remedies.Retry, "Try again")],
                ProjectStageFailure.CauseNoModel,
                "Waiting for a model",
                cancellationToken);
            return;
        }

        var folder = store.GetProjectFolder(recordingId);
        var spoken = transcript.Segments.Select(s => s.Track).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var tracks = manifest.Tracks
            .Where(t => spoken.Contains(t.Id))
            .Select(t => new WorkerTrack(t.Id, Path.Combine(folder, t.File.Replace('/', Path.DirectorySeparatorChar)), t.StartOffsetMs / 1000.0))
            .Where(t => File.Exists(t.Path))
            .ToList();
        if (tracks.Count == 0)
        {
            await status.RemoveAsync(recordingId, Name, cancellationToken);
            return;
        }

        // The recording's own count and names (Who spoke) come first; Settings' expected speakers is the default.
        var whoSpoke = manifest.Details.WhoSpoke ?? WhoSpoke.Unknown;
        var expected = whoSpoke.EffectiveCount ?? current.ExpectedSpeakers;
        var expectedFrom = whoSpoke.EffectiveCount is not null ? "this recording" : "Settings";
        var knownNames = whoSpoke.Names ?? [];

        // What the diarizer hears does not depend on the count (it always clusters by threshold; the host groups the voices
        // to the count), so tracks heard before with the same models and threshold are not listened to again: the ones a
        // finished pass kept (voices.json), and the ones a stopped pass finished (speakers.partial.json).
        var signature = string.Join('|', segmentation!.Id, embedding!.Id, TranscriptionDefaults.ClusteringThreshold.ToString(CultureInfo.InvariantCulture));
        var partial = await writer.Store.LoadSpeakersPartialAsync(recordingId, cancellationToken);
        if (partial is not null && partial.Signature != signature)
        {
            writer.Store.DeleteSpeakersPartial(recordingId);
            partial = null;
        }

        var heardBefore = await writer.Store.LoadVoicesAsync(recordingId, cancellationToken) is { } voicesBefore && voicesBefore.Signature == signature ? voicesBefore.Tracks : [];
        var wanted = tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var done = (partial?.Tracks ?? []).Where(t => wanted.Contains(t.TrackId)).ToList();
        var continued = done.Count;
        done.AddRange(heardBefore.Where(t => wanted.Contains(t.TrackId) && done.All(d => d.TrackId != t.TrackId)));
        var reused = done.Count - continued;
        var elapsedBefore = partial?.ElapsedMs ?? 0;
        var remaining = tracks.Where(t => done.All(d => d.TrackId != t.Id)).ToList();
        var job = new DiarizeJob(remaining, segmentationPath, embeddingPath, -1, TranscriptionDefaults.ClusteringThreshold, TranscriptionDefaults.DiarizationThreads);
        var startPercent = (int)Math.Floor(100.0 * done.Count / tracks.Count);
        var progress = new StageStatus(Name, StageStates.Active, startPercent, string.Create(CultureInfo.InvariantCulture, $"{startPercent}% · CPU"));
        manifest = await status.SetAsync(recordingId, progress, cancellationToken);
        await _history.AppendAsync(
            recordingId,
            Name,
            "started",
            remaining.Count == 0 ? "Identifying speakers (from the voices heard before)" : continued > 0 ? "Identifying speakers (continuing where it stopped)" : "Identifying speakers",
            $"sherpa-onnx · {segmentation.Name} + {embedding.Name} · CPU, {TranscriptionDefaults.DiarizationThreads} threads · {HumanFormat.Count(tracks.Count, "track", "tracks")}"
                + (continued > 0 ? $" · {HumanFormat.Count(continued, "track", "tracks")} already done" : string.Empty)
                + (reused > 0 ? $" · {HumanFormat.Count(reused, "track", "tracks")} heard before" : string.Empty)
                + (expected is { } count ? string.Create(CultureInfo.InvariantCulture, $" · {count} expected ({expectedFrom})") : string.Empty)
                + (knownNames.Count > 0 ? $" · {HumanFormat.Count(knownNames.Count, "name", "names")} given" : string.Empty));
        LogStarting(recordingId, tracks.Count, done.Count);

        var stopwatch = Stopwatch.StartNew();
        DiarizeResult diarization;
        using var leaseSegmentation = models.Use(segmentation.Id);
        using var leaseEmbedding = models.Use(embedding.Id);
        try
        {
            if (remaining.Count == 0)
            {
                diarization = new DiarizeResult([], 0, 0);
            }
            else
            {
                var result = await workers.RunAsync(
                    new WorkerJob(WorkerJobKinds.Diarize, Diarize: job),
                    async reply =>
                    {
                        if (reply.Type == WorkerMessageTypes.Diarized && reply.Diarized is { } finished && done.All(d => d.TrackId != finished.TrackId))
                        {
                            done.Add(finished);
                            await writer.Store.SaveSpeakersPartialAsync(
                                recordingId,
                                new SpeakersPartial(SpeakersPartial.CurrentSchemaVersion, signature, done.ToList(), elapsedBefore + stopwatch.ElapsedMilliseconds),
                                CancellationToken.None);
                        }
                        else if (reply.Type == WorkerMessageTypes.Progress && reply.Percent is { } percent)
                        {
                            // The worker's percent covers the tracks it was given; the stage's covers every track.
                            var already = tracks.Count - remaining.Count;
                            var overall = 100.0 * (already + (percent / 100.0 * remaining.Count)) / tracks.Count;
                            var whole = (int)Math.Clamp(Math.Floor(overall), 0, 99);
                            status.PublishProgress(manifest, progress with { Percent = whole, Label = string.Create(CultureInfo.InvariantCulture, $"{whole}% · CPU") });
                        }
                    },
                    cancellationToken);
                diarization = result.Diarization!;
            }
        }
        catch (OperationCanceledException) when (run.StopReason == StageStopReason.Cancelled)
        {
            await FailAsync(recordingId, "Speaker identification was cancelled.", Kept(done.Count), [new Remedy(Remedies.Retry, "Identify speakers")], ProjectStageFailure.CauseCancelled, "Cancelled", CancellationToken.None);
            return;
        }
        catch (WorkerCrashedException crash)
        {
            await FailAsync(
                recordingId,
                string.Create(CultureInfo.InvariantCulture, $"Speaker identification stopped: the engine closed unexpectedly (code 0x{crash.ExitCode:X8})."),
                Kept(done.Count),
                [new Remedy(Remedies.Retry, "Try again")],
                ProjectStageFailure.CauseCrashed,
                "Speakers failed",
                CancellationToken.None);
            return;
        }
        catch (WorkerJobException error)
        {
            await FailAsync(recordingId, $"Speaker identification stopped: {error.Message.TrimEnd('.')}.", Kept(done.Count), [new Remedy(Remedies.Retry, "Try again")], ProjectStageFailure.CauseEngine, "Speakers failed", CancellationToken.None);
            return;
        }
        catch (WorkerUnavailableException)
        {
            await FailAsync(recordingId, "Speaker identification could not start: the worker is missing from this installation.", "The transcript is kept without speakers. Reinstalling Memento restores the worker.", [new Remedy(Remedies.Retry, "Try again")], ProjectStageFailure.CauseNoWorker, "Speakers failed", CancellationToken.None);
            return;
        }

        // Every track: the ones heard before or finished before (and reported as they finished) and the job's result.
        var all = done.Concat(diarization.Tracks.Where(t => done.All(d => d.TrackId != t.TrackId))).ToList();
        var audioSeconds = all.Sum(t => t.AudioSeconds) is > 0 and var sum ? sum : diarization.AudioSeconds;
        IReadOnlyList<Speaker> speakers = [];
        IReadOnlyList<VoiceCluster> voices = [];
        var merged = 0;
        var naming = new SpeakerNamer.Result([], 0, 0);
        var saved = await writer.UpdateAsync(
            recordingId,
            TranscriptChangeReasons.Speakers,
            latest =>
            {
                if (latest is null)
                {
                    return null;
                }

                // Lines edited meanwhile keep their text; only speaker fields are written. Names given before carry over to
                // the voice that took their lines, then the recording's known names go to the others.
                var assigned = SpeakerAssigner.Assign(
                    latest.Segments,
                    all,
                    expected,
                    expected is null ? TranscriptionDefaults.JoinSimilarity : null,
                    TranscriptionDefaults.MinSpeakerSeconds,
                    TranscriptionDefaults.FoldSimilarity);
                naming = SpeakerNamer.Name(assigned.Speakers, assigned.Segments, latest.Speakers, latest.Segments, knownNames);
                speakers = naming.Speakers;
                voices = assigned.Voices ?? [];
                merged = assigned.Merged;
                return latest with { Segments = assigned.Segments, Speakers = naming.Speakers };
            },
            CancellationToken.None);
        await writer.Store.SaveVoicesAsync(recordingId, new VoicesDocument(VoicesDocument.CurrentSchemaVersion, embedding.Id, signature, all, voices), CancellationToken.None);
        writer.Store.DeleteSpeakersPartial(recordingId);
        await status.SetAsync(recordingId, new StageStatus(Name, StageStates.Done, null, "Done"), CancellationToken.None);

        var total = Math.Max(1, speakers.Sum(s => s.TalkTimeMs));
        var shares = string.Join(", ", speakers.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Name} {100.0 * s.TalkTimeMs / total:0}%")));
        var elapsed = (elapsedBefore + stopwatch.ElapsedMilliseconds) / 1000.0;
        var found = all.Sum(t => t.Turns.Select(u => u.Speaker).Distinct().Count());
        await _history.AppendAsync(
            recordingId,
            Name,
            "completed",
            $"Found {HumanFormat.Count(speakers.Count, "speaker", "speakers")}",
            string.Join(
                " · ",
                new[]
                {
                    $"sherpa-onnx · {segmentation.Name} + {embedding.Name} · CPU",
                    string.Create(CultureInfo.InvariantCulture, $"{HumanFormat.Clock((long)(audioSeconds * 1000))} of audio in {elapsed:0.0} s"),
                    merged > 0
                        ? string.Create(CultureInfo.InvariantCulture, $"{HumanFormat.Count(found, "voice", "voices")} heard, grouped into {HumanFormat.Count(speakers.Count, "speaker", "speakers")}") + (expected is { } wantedCount ? string.Create(CultureInfo.InvariantCulture, $" ({wantedCount} expected, {expectedFrom})") : string.Empty)
                        : string.Empty,
                    naming.Carried + naming.Known > 0
                        ? string.Join(" and ", new[] { naming.Carried > 0 ? $"{HumanFormat.Count(naming.Carried, "name", "names")} kept from before" : string.Empty, naming.Known > 0 ? $"{HumanFormat.Count(naming.Known, "name", "names")} from Who spoke" : string.Empty }.Where(p => p.Length > 0))
                        : string.Empty,
                    "talk time: " + (speakers.Count == 0 ? "none" : shares),
                }.Where(p => p.Length > 0)));
        if (current.RememberRenamed)
        {
            await _history.AppendAsync(
                recordingId,
                Name,
                "info",
                "Renamed speakers are not remembered yet",
                "\"Remember renamed speakers\" is saved in Settings but not applied in this version; rename the speakers here.");
        }

        LogCompleted(recordingId, speakers.Count, saved?.Version ?? 0);
    }

    private static string Kept(int tracksDone) =>
        tracksDone == 0
            ? "The transcript is kept without speakers."
            : $"The transcript is kept without speakers; {HumanFormat.Count(tracksDone, "track", "tracks")} already done {(tracksDone == 1 ? "is" : "are")} kept, and trying again continues with the others.";

    private async Task FailAsync(string recordingId, string message, string kept, IReadOnlyList<Remedy> remedies, string cause, string label, CancellationToken cancellationToken)
    {
        var failure = new ProjectStageFailure(Name, message, kept, remedies, cause, time.GetLocalNow());
        await status.FailAsync(recordingId, failure, label, cancellationToken);
        await _history.AppendAsync(recordingId, Name, "failed", cause == ProjectStageFailure.CauseNoModel ? "Waiting for the speaker models" : "Speaker identification failed", message + " " + kept);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: {Speakers} speakers identified (transcript version {Version})")]
    private partial void LogCompleted(string recordingId, int speakers, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: identifying speakers on {Tracks} tracks ({Done} already done)")]
    private partial void LogStarting(string recordingId, int tracks, int done);
}
