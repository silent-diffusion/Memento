using System.Diagnostics;
using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recording;

/// <summary>
/// The <c>stored</c> stage: runs <see cref="ITrackFinalizer"/> for a stopped (or recovered) recording and records
/// the outcome in the manifest, <c>history.jsonl</c>, the index and the UI (<c>processing.progress</c>,
/// <c>library.changed</c>). Order on success: manifest saved → <c>recording.state.json</c> deleted → replaced capture
/// WAVs deleted, so an interruption never leaves a manifest pointing at missing audio. On failure everything is kept,
/// including <c>recording.state.json</c>, so the next launch tries again.
/// </summary>
public sealed partial class ProjectFinalizationService(
    IProjectStore store,
    ProjectCatalog catalog,
    ITrackFinalizer finalizer,
    BridgeEventPublisher publisher,
    TimeProvider time,
    ILogger<ProjectFinalizationService> logger,
    IEnumerable<IProcessingStage>? stages = null,
    ISettingsStore? settings = null)
{
    private readonly ILogger<ProjectFinalizationService> _logger = logger;
    private readonly IReadOnlyList<IProcessingStage> _stages = stages?.ToList() ?? [];

    /// <summary>
    /// The stages that follow <c>stored</c> with the current settings, marked queued in the same write that marks
    /// <c>stored</c> done, so the Library's processing card does not vanish for a moment in between (the orchestrator
    /// then schedules them, <see cref="ProcessingOrchestrator.EnqueueAfterStoredAsync"/>).
    /// </summary>
    private IReadOnlyList<StageStatus> WithFollowingQueued(IReadOnlyList<StageStatus> stages)
    {
        if (settings is null)
        {
            return stages;
        }

        var current = settings.Current;
        foreach (var stage in _stages.Where(s => s.AppliesTo(current)))
        {
            if (StageList.Find(stages, stage.Name) is not { State: StageStates.Done })
            {
                stages = StageList.With(stages, StageStatusWriter.QueuedStatus(stage.Name));
            }
        }

        return stages;
    }

    /// <param name="finalState"><see cref="ProjectStates.Ready"/> or <see cref="ProjectStates.Recovered"/>.</param>
    public async Task<ProjectManifest> FinalizeAsync(string recordingId, string finalState, CancellationToken cancellationToken)
    {
        var manifest = await catalog.UpdateAsync(
            recordingId,
            m => m with { State = ProjectStates.Finalizing, Stages = WithStored(m.Stages, StageStates.Active, 0, "Saving tracks") },
            cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, manifest.Stages));
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(time.GetLocalNow(), StageNames.Stored, "started", "Saving tracks", null),
            cancellationToken);

        var folder = store.GetProjectFolder(recordingId);
        var inputs = manifest.Tracks
            .Select(t => new FinalizeTrackInput(t.Id, t.CaptureFile ?? t.File, t.StartOffsetMs, t.EndedEarlyAtMs))
            .ToList();

        // Always lossless here; a smaller format from Settings is the optimize stage's job, after every other stage.
        var storage = StorageFormat.Lossless;
        var stopwatch = Stopwatch.StartNew();
        FinalizedAudio audio;
        try
        {
            var progress = new StageProgress(recordingId, manifest.Stages, publisher);
            audio = await finalizer.FinalizeAsync(new FinalizeRequest(folder, inputs, storage), progress, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return await RecordFailureAsync(recordingId, ex, cancellationToken);
        }

        var finalized = audio.Tracks.ToDictionary(t => t.TrackId, StringComparer.Ordinal);
        var integrity = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var track in audio.Tracks)
        {
            integrity[track.File] = track.Sha256;
        }

        integrity[audio.Mix.File] = audio.Mix.Sha256;
        foreach (var (file, sha256) in audio.ExtraHashes)
        {
            integrity[file] = sha256;
        }

        var saved = await catalog.UpdateAsync(
            recordingId,
            m => m with
            {
                State = finalState,
                DurationMs = m.DurationMs > 0 ? m.DurationMs : audio.Mix.DurationMs,
                Tracks = m.Tracks.Select(t => finalized.TryGetValue(t.Id, out var f)
                    ? t with
                    {
                        File = f.File,
                        CaptureFile = string.Equals(f.File, t.CaptureFile ?? t.File, StringComparison.OrdinalIgnoreCase) ? t.CaptureFile : null,
                        Codec = f.Codec,
                        SampleRate = f.SampleRate,
                        Channels = f.Channels,
                        DurationMs = f.DurationMs,
                        Sha256 = f.Sha256,
                    }
                    : t).ToList(),
                Mix = new ProjectMix(audio.Mix.File, audio.Mix.Codec, audio.Mix.SampleRate, audio.Mix.Channels, audio.Mix.DurationMs, audio.Mix.Sha256),
                Peaks = audio.PeaksFile,
                Integrity = new ProjectIntegrity { ComputedAt = audio.ComputedAt, Files = integrity },
                Stages = WithFollowingQueued(WithStored(m.Stages, StageStates.Done, null, "Done")),
            },
            cancellationToken);

        var size = audio.Tracks.Sum(t => t.SizeBytes) + audio.Mix.SizeBytes;
        var detail = string.Join(
            " · ",
            new[]
            {
                string.Create(CultureInfo.InvariantCulture, $"{audio.Codec.ToUpperInvariant()}: {string.Join(", ", audio.Tracks.Select(t => t.File))}"),
                string.Create(CultureInfo.InvariantCulture, $"mix {audio.Mix.File}, {audio.Mix.SampleRate / 1000.0:0.#} kHz"),
                "SHA-256 computed for every file",
                string.Create(CultureInfo.InvariantCulture, $"took {stopwatch.Elapsed.TotalSeconds:0.0} s"),
            }.Concat(audio.Notes));
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(
                audio.ComputedAt,
                StageNames.Stored,
                "completed",
                $"Stored {HumanFormat.Count(audio.Tracks.Count, "track", "tracks")} · {HumanFormat.Bytes(size)}",
                detail),
            cancellationToken);
        foreach (var warning in audio.Warnings)
        {
            await AppendQuietlyAsync(
                recordingId,
                new HistoryEntry(audio.ComputedAt, StageNames.Stored, "info", warning.Summary, warning.Detail),
                cancellationToken);
        }

        store.DeleteRecordingState(recordingId);
        DeleteReplacedCaptures(folder, audio.ObsoleteCaptureFiles);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
        await catalog.TouchedAsync(recordingId, cancellationToken);
        LogStored(recordingId, audio.Tracks.Count, size, stopwatch.ElapsedMilliseconds);
        return saved;
    }

    /// <summary>History is a log; failing to append one line must not fail saving the audio.</summary>
    private async Task AppendQuietlyAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendHistoryAsync(recordingId, entry, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    internal static IReadOnlyList<StageStatus> WithStored(IReadOnlyList<StageStatus> stages, string state, int? percent, string label)
    {
        var stored = new StageStatus(StageNames.Stored, state, percent, label);
        var list = stages.Where(s => s.Stage != StageNames.Stored).ToList();
        list.Insert(0, stored);
        return list;
    }

    private async Task<ProjectManifest> RecordFailureAsync(string recordingId, Exception exception, CancellationToken cancellationToken)
    {
        var diskFull = DiskErrors.IsDiskFull(exception);
        LogFailed(exception, recordingId, diskFull);
        var reason = diskFull
            ? "The drive is full. The recorded audio is kept as WAV; Memento finishes saving at the next start once there is room."
            : "The recorded audio is kept as WAV; Memento tries again at the next start. The error was written to the Memento log.";
        var saved = await catalog.UpdateAsync(
            recordingId,
            m => m with
            {
                State = ProjectStates.Failed,
                Stages = WithStored(m.Stages, StageStates.Failed, null, diskFull ? "Saving failed · drive full" : "Saving failed"),
            },
            cancellationToken);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(time.GetLocalNow(), StageNames.Stored, "failed", "Saving the tracks failed", reason),
            cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
        return saved;
    }

    private void DeleteReplacedCaptures(string folder, IReadOnlyList<string> files)
    {
        foreach (var relative in files)
        {
            var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                // The encoded file is complete and hashed; the leftover WAV only costs space.
                LogCaptureNotDeleted(ex, relative);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId} stored: {Tracks} tracks, {Bytes} bytes in {ElapsedMs} ms")]
    private partial void LogStored(string recordingId, int tracks, long bytes, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storing recording {RecordingId} failed (disk full: {DiskFull}); the capture files are kept")]
    private partial void LogFailed(Exception exception, string recordingId, bool diskFull);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replaced capture file {File} could not be deleted")]
    private partial void LogCaptureNotDeleted(Exception exception, string file);

    /// <summary>Publishes <c>processing.progress</c> when the stored stage's percentage changes.</summary>
    private sealed class StageProgress(string recordingId, IReadOnlyList<StageStatus> stages, BridgeEventPublisher publisher) : IProgress<int>
    {
        private int _last = -1;

        public void Report(int value)
        {
            var percent = Math.Clamp(value, 0, 100);
            if (Interlocked.Exchange(ref _last, percent) == percent)
            {
                return;
            }

            var label = string.Create(CultureInfo.InvariantCulture, $"{percent}% · saving tracks");
            publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, WithStored(stages, StageStates.Active, percent, label)));
        }
    }
}
