using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recovery;

/// <summary>
/// Crash recovery at launch (ARCHITECTURE.md §5.8). Every project with a <c>recording.state.json</c> is repaired
/// (WAV headers rewritten from the file length), finalized, marked <c>recovered</c> and offered in the recovery
/// dialog. It never asks whether to keep a recording and never deletes anything. Runs before the window shows.
/// </summary>
public sealed partial class RecoveryService(
    IProjectStore store,
    ILibraryIndex index,
    ProjectCatalog catalog,
    ProjectFinalizationService finalization,
    ProcessingOrchestrator processing,
    TimeProvider time,
    ILogger<RecoveryService> logger)
{
    private readonly ILogger<RecoveryService> _logger = logger;

    /// <summary>Recovers every interrupted project; returns the ids that were recovered.</summary>
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken)
    {
        var recovered = new List<string>();
        foreach (var id in store.ListIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!store.HasRecordingState(id))
            {
                continue;
            }

            try
            {
                if (await RecoverAsync(id, cancellationToken))
                {
                    recovered.Add(id);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ProjectNotFoundException or ProjectSchemaException)
            {
                // Leave it exactly as it is; the next launch tries again.
                LogRecoveryFailed(ex, id);
            }
        }

        return recovered;
    }

    /// <summary>Recovered projects the user has not dismissed, newest first (<c>recovery.list</c>).</summary>
    public async Task<IReadOnlyList<RecoveryItem>> ListAsync(CancellationToken cancellationToken)
    {
        var items = new List<RecoveryItem>();
        foreach (var id in await index.ListIdsByStateAsync(ProjectStates.Recovered, cancellationToken))
        {
            ProjectManifest manifest;
            try
            {
                manifest = await store.LoadAsync(id, cancellationToken);
            }
            catch (ProjectNotFoundException)
            {
                continue;
            }

            if (manifest.Recovery is { Acknowledged: false } recovery)
            {
                items.Add(new RecoveryItem(
                    manifest.Id,
                    manifest.Details.Title,
                    manifest.CreatedAt,
                    recovery.TracksIntact,
                    recovery.TracksTotal,
                    recovery.LastCheckpointAt,
                    recovery.RecoveredDurationMs,
                    recovery.MayBeMissingMs));
            }
        }

        return items;
    }

    /// <summary>Dismisses the dialog for one project (<c>recovery.acknowledge</c>). The recording itself is untouched.</summary>
    /// <exception cref="ProjectNotFoundException">No such project.</exception>
    public async Task AcknowledgeAsync(string recordingId, CancellationToken cancellationToken)
    {
        await catalog.UpdateAsync(
            recordingId,
            m => m.Recovery is { } recovery ? m with { Recovery = recovery with { Acknowledged = true } } : m,
            cancellationToken);
    }

    private async Task<bool> RecoverAsync(string id, CancellationToken cancellationToken)
    {
        var manifest = await store.LoadAsync(id, cancellationToken);
        var state = await store.ReadRecordingStateAsync(id, cancellationToken);
        var interrupted = manifest.State is ProjectStates.Recording or ProjectStates.Finalizing;
        var folder = store.GetProjectFolder(id);

        // The state file is the most complete track list; fall back to the manifest if it was unreadable.
        var stateTracks = state?.Tracks ?? manifest.Tracks.Select(t => new RecordingStateTrack(
            t.Id, t.SourceId, t.SourceKind, t.Name, t.CaptureFile ?? t.File, t.SampleRate, t.Channels, t.BitsPerSample,
            t.SampleEncoding, t.StartOffsetMs, 0, t.EndedEarlyAtMs, t.EndReason)).ToList();

        var tracks = new List<ProjectTrack>();
        var intact = 0;
        long recoveredMs = 0;
        long mayBeMissingMs = 0;
        var intervalMs = (state?.CheckpointSeconds ?? 30) * 1000L;
        foreach (var track in stateTracks)
        {
            // A long track continues in tracks/<id>.part2.wav, …; only the part being written can need repair, but
            // checking every part costs one header read each.
            var parts = CaptureParts.Find(folder, track.File);
            var repair = WavRepair.Repair(Path.Combine(folder, track.File.Replace('/', Path.DirectorySeparatorChar)), SessionTrackMapper.FormatOf(track));
            var durationMs = repair.DurationMs;
            var dataBytes = repair.DataBytes;
            foreach (var part in parts.Skip(1))
            {
                var partRepair = WavRepair.Repair(Path.Combine(folder, part.Replace('/', Path.DirectorySeparatorChar)), SessionTrackMapper.FormatOf(track));
                if (!partRepair.Succeeded)
                {
                    LogTrackUnrepairable(id, $"{track.TrackId} ({part})", partRepair.Problem ?? "unknown");
                    continue;
                }

                durationMs += partRepair.DurationMs;
                dataBytes += partRepair.DataBytes;
            }

            if (repair.Succeeded && dataBytes > 0)
            {
                intact++;
            }

            if (!repair.Succeeded)
            {
                LogTrackUnrepairable(id, track.TrackId, repair.Problem ?? "unknown");
            }
            else if (repair.Changed)
            {
                LogTrackRepaired(id, track.TrackId, repair.DataBytes, repair.TruncatedBytes);
            }

            recoveredMs = Math.Max(recoveredMs, track.StartOffsetMs + durationMs);
            var stillOpen = track.EndedAtMs is null;
            if (interrupted && stillOpen && state?.State == "recording" && repair.Succeeded)
            {
                // The crash happened before the next checkpoint was due; anything after the data on disk is lost.
                var checkpointedMs = repair.Format!.BytesToMilliseconds(track.BytesAtCheckpoint);
                var beyond = Math.Max(0, durationMs - checkpointedMs);
                var bound = Math.Max(0, intervalMs - beyond);
                if (beyond > 0 && state.FlushIntervalMs is { } flushMs and > 0)
                {
                    // Audio past the checkpoint reached the disk, so the writers were still flushing on their own
                    // cadence when the app went down: at most one flush interval (plus a packet) is gone.
                    bound = Math.Min(bound, flushMs + 100);
                }

                mayBeMissingMs = Math.Max(mayBeMissingMs, bound);
            }

            var existing = manifest.Tracks.FirstOrDefault(t => t.Id == track.TrackId);
            tracks.Add((existing ?? new ProjectTrack
            {
                Id = track.TrackId,
                SourceId = track.SourceId,
                SourceKind = track.SourceKind,
                Name = track.Name,
                File = track.File,
            }) with
            {
                File = track.File,
                CaptureFile = track.File,
                Codec = PassThroughWavEncoder.WavCodec,
                SampleRate = track.SampleRate,
                Channels = track.Channels,
                BitsPerSample = track.BitsPerSample,
                SampleEncoding = track.SampleEncoding,
                StartOffsetMs = track.StartOffsetMs,
                DurationMs = durationMs,
                EndedEarlyAtMs = track.EndedAtMs,
                EndReason = track.EndReason,
                Sha256 = null,
            });
        }

        var now = time.GetLocalNow();
        var recovery = interrupted
            ? new ProjectRecovery(now, state?.LastCheckpointAt, intact, stateTracks.Count, recoveredMs, mayBeMissingMs, Acknowledged: false)
            : manifest.Recovery;
        await catalog.UpdateAsync(
            id,
            m => m with
            {
                Tracks = tracks,
                Pauses = state?.Pauses ?? m.Pauses,
                DurationMs = interrupted ? recoveredMs : m.DurationMs,
                Recovery = recovery,
            },
            cancellationToken);

        if (interrupted)
        {
            await store.AppendHistoryAsync(
                id,
                new HistoryEntry(
                    now,
                    "recovered",
                    "info",
                    "Recovered after Memento closed during recording",
                    $"{intact} of {HumanFormat.Count(stateTracks.Count, "track", "tracks")} intact · {HumanFormat.Clock(recoveredMs)} recovered"
                        + (mayBeMissingMs > 0 ? $" · up to {HumanFormat.Clock(mayBeMissingMs)} after the last checkpoint may be missing" : " · nothing is missing")),
                cancellationToken);
        }

        var finalState = interrupted || manifest.Recovery is not null ? ProjectStates.Recovered : ProjectStates.Ready;
        var result = await finalization.FinalizeAsync(id, finalState, cancellationToken);
        if (result.State != ProjectStates.Failed)
        {
            // Runs in the background after the window shows.
            await processing.EnqueueAfterStoredAsync(id, cancellationToken);
        }

        LogRecovered(id, interrupted, intact, stateTracks.Count, recoveredMs, mayBeMissingMs, result.State);
        return interrupted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovery: project {RecordingId} (interrupted: {Interrupted}) finalized with {Intact}/{Total} tracks intact, {RecoveredMs} ms recovered, up to {MissingMs} ms missing; state {State}")]
    private partial void LogRecovered(string recordingId, bool interrupted, int intact, int total, long recoveredMs, long missingMs, string state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovery: project {RecordingId} track {TrackId} header rewritten ({Bytes} bytes of samples, {Truncated} trailing bytes cut)")]
    private partial void LogTrackRepaired(string recordingId, string trackId, long bytes, long truncated);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovery: project {RecordingId} track {TrackId} could not be repaired: {Problem}")]
    private partial void LogTrackUnrepairable(string recordingId, string trackId, string problem);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recovery of project {RecordingId} failed; it is left untouched for the next launch")]
    private partial void LogRecoveryFailed(Exception exception, string recordingId);
}
