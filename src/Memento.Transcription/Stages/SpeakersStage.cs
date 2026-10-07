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
/// (pyannote segmentation 3.0 + the voice model from Settings, clustering threshold 0.8, 4 threads), then speakers are
/// assigned to segments by time overlap within each track. The expected speaker count is applied only when one track
/// has speech (it says nothing about how many people each track holds).
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

        var clusters = current.ExpectedSpeakers is { } expected && tracks.Count == 1 ? expected : -1;
        var job = new DiarizeJob(tracks, segmentationPath, embeddingPath, clusters, TranscriptionDefaults.ClusteringThreshold, TranscriptionDefaults.DiarizationThreads);
        var progress = new StageStatus(Name, StageStates.Active, 0, "0% · CPU");
        manifest = await status.SetAsync(recordingId, progress, cancellationToken);
        await _history.AppendAsync(
            recordingId,
            Name,
            "started",
            "Identifying speakers",
            $"sherpa-onnx · {segmentation!.Name} + {embedding!.Name} · CPU, {TranscriptionDefaults.DiarizationThreads} threads · {HumanFormat.Count(tracks.Count, "track", "tracks")}"
                + (clusters > 0 ? string.Create(CultureInfo.InvariantCulture, $" · {clusters} expected") : string.Empty));

        var stopwatch = Stopwatch.StartNew();
        WorkerReply result;
        using var leaseSegmentation = models.Use(segmentation.Id);
        using var leaseEmbedding = models.Use(embedding.Id);
        try
        {
            result = await workers.RunAsync(
                new WorkerJob(WorkerJobKinds.Diarize, Diarize: job),
                reply =>
                {
                    if (reply.Type == WorkerMessageTypes.Progress && reply.Percent is { } percent)
                    {
                        var whole = (int)Math.Clamp(Math.Floor(percent), 0, 99);
                        status.PublishProgress(manifest, progress with { Percent = whole, Label = string.Create(CultureInfo.InvariantCulture, $"{whole}% · CPU") });
                    }

                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (run.StopReason == StageStopReason.Cancelled)
        {
            await FailAsync(recordingId, "Speaker identification was cancelled.", "The transcript is kept without speakers.", [new Remedy(Remedies.Retry, "Identify speakers")], ProjectStageFailure.CauseCancelled, "Cancelled", CancellationToken.None);
            return;
        }
        catch (WorkerCrashedException crash)
        {
            await FailAsync(
                recordingId,
                string.Create(CultureInfo.InvariantCulture, $"Speaker identification stopped: the engine closed unexpectedly (code 0x{crash.ExitCode:X8})."),
                "The transcript is kept without speakers.",
                [new Remedy(Remedies.Retry, "Try again")],
                ProjectStageFailure.CauseCrashed,
                "Speakers failed",
                CancellationToken.None);
            return;
        }
        catch (WorkerJobException error)
        {
            await FailAsync(recordingId, $"Speaker identification stopped: {error.Message.TrimEnd('.')}.", "The transcript is kept without speakers.", [new Remedy(Remedies.Retry, "Try again")], ProjectStageFailure.CauseEngine, "Speakers failed", CancellationToken.None);
            return;
        }
        catch (WorkerUnavailableException)
        {
            await FailAsync(recordingId, "Speaker identification could not start: the worker is missing from this installation.", "The transcript is kept without speakers. Reinstalling Memento restores the worker.", [new Remedy(Remedies.Retry, "Try again")], ProjectStageFailure.CauseNoWorker, "Speakers failed", CancellationToken.None);
            return;
        }

        var diarization = result.Diarization!;
        IReadOnlyList<Speaker> speakers = [];
        var saved = await writer.UpdateAsync(
            recordingId,
            TranscriptChangeReasons.Speakers,
            latest =>
            {
                if (latest is null)
                {
                    return null;
                }

                // Lines edited meanwhile keep their text; only speaker fields are written.
                var assigned = SpeakerAssigner.Assign(latest.Segments, diarization.Tracks);
                speakers = assigned.Speakers;
                return latest with { Segments = assigned.Segments, Speakers = assigned.Speakers };
            },
            CancellationToken.None);
        await status.SetAsync(recordingId, new StageStatus(Name, StageStates.Done, null, "Done"), CancellationToken.None);

        var total = Math.Max(1, speakers.Sum(s => s.TalkTimeMs));
        var shares = string.Join(", ", speakers.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Name} {100.0 * s.TalkTimeMs / total:0}%")));
        var elapsed = stopwatch.Elapsed.TotalSeconds;
        await _history.AppendAsync(
            recordingId,
            Name,
            "completed",
            $"Found {HumanFormat.Count(speakers.Count, "speaker", "speakers")}",
            string.Join(
                " · ",
                $"sherpa-onnx · {segmentation.Name} + {embedding.Name} · CPU",
                string.Create(CultureInfo.InvariantCulture, $"{HumanFormat.Clock((long)(diarization.AudioSeconds * 1000))} of audio in {elapsed:0.0} s"),
                "talk time: " + (speakers.Count == 0 ? "none" : shares)));
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

    private async Task FailAsync(string recordingId, string message, string kept, IReadOnlyList<Remedy> remedies, string cause, string label, CancellationToken cancellationToken)
    {
        var failure = new ProjectStageFailure(Name, message, kept, remedies, cause, time.GetLocalNow());
        await status.FailAsync(recordingId, failure, label, cancellationToken);
        await _history.AppendAsync(recordingId, Name, "failed", cause == ProjectStageFailure.CauseNoModel ? "Waiting for the speaker models" : "Speaker identification failed", message + " " + kept);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: {Speakers} speakers identified (transcript version {Version})")]
    private partial void LogCompleted(string recordingId, int speakers, int version);
}
