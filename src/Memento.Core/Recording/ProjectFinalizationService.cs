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
    private IReadOnlyList<StageStatus> WithFollowingQueued(ProjectManifest manifest, IReadOnlyList<StageStatus> stages)
    {
        if (settings is null)
        {
            return stages;
        }

        var current = settings.Current;
        foreach (var stage in _stages.Where(s => s.AppliesTo(current) && s.AppliesTo(manifest)))
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

        // 2.1, "Resume recording": tracks of earlier segments are finalized (hashed) and read-only; only the new
        // segment's capture files are encoded, and the mix is then built again over the whole timeline.
        var resumed = manifest.Tracks.Any(t => t.Sha256 is not null) && manifest.Tracks.Any(t => t.Sha256 is null);
        var inputs = manifest.Tracks
            .Where(t => !resumed || t.Sha256 is null)
            .Select(t => new FinalizeTrackInput(t.Id, t.CaptureFile ?? t.File, t.StartOffsetMs, t.EndedEarlyAtMs))
            .ToList();

        // Always lossless here; a smaller format from Settings is the optimize stage's job, after every other stage.
        var storage = StorageFormat.Lossless;
        var stopwatch = Stopwatch.StartNew();
        FinalizedAudio audio;
        try
        {
            var progress = new StageProgress(recordingId, manifest.Stages, publisher);
            // "Even out source levels" (Settings › Recording › Storage, on by default) applies to the mix only.
            var evenLevels = settings?.Current.Recording.Storage.EvenLevels ?? false;
            audio = await finalizer.FinalizeAsync(new FinalizeRequest(folder, inputs, storage) { EvenLevels = evenLevels }, progress, cancellationToken);
            if (resumed)
            {
                audio = await MixOverSegmentsAsync(manifest, folder, audio, evenLevels, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return await RecordFailureAsync(recordingId, ex, cancellationToken);
        }

        var finalized = audio.Tracks.ToDictionary(t => t.TrackId, StringComparer.Ordinal);
        var integrity = resumed
            ? new Dictionary<string, string>(manifest.Integrity.Files, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        if (resumed && manifest.Mix is { } previousMix)
        {
            integrity.Remove(previousMix.File);
        }

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
                Mix = new ProjectMix(audio.Mix.File, audio.Mix.Codec, audio.Mix.SampleRate, audio.Mix.Channels, audio.Mix.DurationMs, audio.Mix.Sha256)
                {
                    EvenLevels = audio.MixGains is { } gains ? new ProjectMixLevels(audio.ComputedAt, gains) : null,
                },
                Peaks = audio.PeaksFile,
                Integrity = new ProjectIntegrity { ComputedAt = audio.ComputedAt, Files = integrity },
                Stages = WithFollowingQueued(m, WithStored(resumed ? WithStagesAgain(m.Stages) : m.Stages, StageStates.Done, null, "Done")),
                Processing = resumed && StageList.Find(m.Stages, StageNames.Transcript) is { State: StageStates.Done }
                    ? (m.Processing ?? new ProcessingRequest()) with { Retranscribe = true }
                    : m.Processing,
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
            }.Concat(audio.MixGains is { } evened ? new[] { MixLevels.Describe(evened, id => saved.Tracks.FirstOrDefault(t => t.Id == id)?.Name ?? id) } : Array.Empty<string>()).Concat(audio.Notes));
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

        if (resumed)
        {
            var again = saved.Stages.Where(s => s.Stage is not (StageNames.Stored or StageNames.Mix) && StageStates.IsRunning(s.State)).Select(s => s.Stage).ToList();
            await AppendQuietlyAsync(
                recordingId,
                new HistoryEntry(
                    audio.ComputedAt,
                    StageNames.Stored,
                    "info",
                    $"Resumed recording saved · {HumanFormat.Count(saved.Segments.Count, "segment", "segments")}, {HumanFormat.Clock(saved.DurationMs)} in all",
                    "The earlier tracks were kept as they were and the mix was built again over the whole recording"
                        + (again.Count > 0
                            ? $" · {string.Join(", ", again)} run again over the whole recording, because a pass over only the new segment is not available; the current transcript stays until the new one replaces it"
                            : string.Empty)),
                cancellationToken);
            if (manifest.Mix is { } oldMix && !string.Equals(oldMix.File, audio.Mix.File, StringComparison.OrdinalIgnoreCase))
            {
                // A smaller mix (mix.mp3) the whole-timeline mix replaced goes only now that the manifest points at the new one.
                try
                {
                    var oldPath = ProjectPaths.Resolve(folder, oldMix.File);
                    ClearReadOnly(oldPath);
                    File.Delete(oldPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    LogCaptureNotDeleted(ex, oldMix.File);
                }
            }
        }

        store.DeleteRecordingState(recordingId);
        DeleteReplacedCaptures(folder, audio.ObsoleteCaptureFiles);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
        await catalog.TouchedAsync(recordingId, cancellationToken);
        LogStored(recordingId, audio.Tracks.Count, size, stopwatch.ElapsedMilliseconds);
        return saved;
    }

    /// <summary>
    /// A resumed recording (2.1): the mix <see cref="ITrackFinalizer.FinalizeAsync"/> wrote covers only the new segment, so
    /// every track (the earlier segments' stored files and the new ones) is mixed again over the whole timeline into
    /// <see cref="SegmentsMixStem"/>, which then replaces the mix and the peaks. The tracks are only read.
    /// </summary>
    private async Task<FinalizedAudio> MixOverSegmentsAsync(ProjectManifest manifest, string folder, FinalizedAudio audio, bool evenLevels, CancellationToken cancellationToken)
    {
        var fresh = audio.Tracks.ToDictionary(t => t.TrackId, StringComparer.Ordinal);
        var tracks = manifest.Tracks
            .Where(t => fresh.ContainsKey(t.Id) || t.Sha256 is not null)
            .Select(t => new MixRebuildTrack(t.Id, fresh.TryGetValue(t.Id, out var f) ? f.File : t.File, t.StartOffsetMs, t.EndedEarlyAtMs))
            .ToList();
        var rebuilt = await finalizer.RebuildMixAsync(new MixRebuildRequest(folder, tracks, evenLevels && tracks.Count > 1, SegmentsMixStem), null, cancellationToken);
        var target = ProjectLayout.MixBaseName + Path.GetExtension(rebuilt.Mix.File);
        var targetPath = ProjectPaths.Resolve(folder, target);
        ClearReadOnly(targetPath);
        File.Move(ProjectPaths.Resolve(folder, rebuilt.Mix.File), targetPath, overwrite: true);
        File.SetAttributes(targetPath, File.GetAttributes(targetPath) | FileAttributes.ReadOnly);
        File.Move(ProjectPaths.Resolve(folder, rebuilt.PeaksFile), ProjectPaths.Resolve(folder, audio.PeaksFile), overwrite: true);

        // The segment-only mix finalize wrote under another extension, if any, is not the recording's mix.
        if (!string.Equals(audio.Mix.File, target, StringComparison.OrdinalIgnoreCase))
        {
            var segmentMix = ProjectPaths.Resolve(folder, audio.Mix.File);
            ClearReadOnly(segmentMix);
            File.Delete(segmentMix);
        }

        return audio with
        {
            Mix = rebuilt.Mix with { File = target },
            MixGains = rebuilt.Gains,
            Notes = audio.Notes.Append($"mix built again over {HumanFormat.Count(manifest.Segments.Count, "segment", "segments")}").ToList(),
        };
    }

    /// <summary>The name of the whole-timeline mix of a resumed recording until it replaces the mix.</summary>
    public const string SegmentsMixStem = "mix.segments";

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>
    /// After a resumed segment is stored: the stages that already ran over the earlier segments are queued again over
    /// the whole recording (a transcript pass replaces the transcript; it is kept as a version when history is on).
    /// </summary>
    private static List<StageStatus> WithStagesAgain(IReadOnlyList<StageStatus> stages) =>
        stages.Where(s => s.Stage is StageNames.Stored or StageNames.Mix || s.State != StageStates.Done).ToList();

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
            try
            {
                File.Delete(ProjectPaths.Resolve(folder, relative));
            }
            catch (InvalidDataException)
            {
                // Never a file outside the project folder.
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
