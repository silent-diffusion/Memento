using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Video;

/// <summary>
/// "Remove video, keep audio" (2.0, DESIGN.md §18.3, §18.5–§18.7): deletes a recording's video files after the page's
/// confirmation (<c>video.remove</c>) or, when Settings › Storage and history › Remove video older than is set, for
/// recordings older than that (<see cref="SweepAsync"/>). The manifest records it first (<c>videoRemoved</c>, the
/// tracks stay listed without a hash), then the files go, then History says what was freed. Audio tracks, the mix, the
/// transcript and documents are never touched. Never runs while the recording is being recorded or saved, or while
/// its video stage runs.
/// </summary>
public sealed partial class VideoRemovalService(
    IProjectStore store,
    ProjectCatalog catalog,
    RecordingCoordinator recordings,
    ProcessingOrchestrator processing,
    ISettingsStore settings,
    TimeProvider time,
    ILogger<VideoRemovalService> logger) : IDisposable
{
    private static readonly TimeSpan[] DeleteRetryDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly ILogger<VideoRemovalService> _logger = logger;

    public void Dispose() => _oneAtATime.Dispose();

    /// <summary>The video files of <paramref name="manifest"/> on disk, relative to its folder, with their sizes.</summary>
    public static IReadOnlyList<(string File, long Bytes)> VideoFiles(ProjectManifest manifest, string folder)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var result = new List<(string, long)>();
        foreach (var file in manifest.VideoTracks.SelectMany(t => t.Parts.Select(p => p.File).Append(t.File)).Distinct(StringComparer.Ordinal))
        {
            if (!ProjectPaths.IsSafeRelative(file))
            {
                continue;
            }

            var path = ProjectPaths.Resolve(folder, file);
            if (File.Exists(path))
            {
                result.Add((file, new FileInfo(path).Length));
            }
        }

        return result;
    }

    /// <summary>
    /// <c>video.removeEstimate</c>: what Remove video would free, for <paramref name="recordingIds"/>, or (null) every recording
    /// with video older than <paramref name="olderThanDays"/> (null: every one). Settings' "Would free 11.3 GB now, from 3 recordings".
    /// </summary>
    public async Task<(int Recordings, long Bytes)> EstimateAsync(IReadOnlyList<string>? recordingIds, int? olderThanDays, CancellationToken cancellationToken)
    {
        DateTimeOffset? cutoff = recordingIds is null && olderThanDays is { } days ? time.GetUtcNow() - TimeSpan.FromDays(days) : null;
        var ids = recordingIds?.Distinct(StringComparer.Ordinal).ToList() ?? (IReadOnlyList<string>)store.ListIds();
        var recordingsFound = 0;
        long bytes = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = ProjectId.IsValid(id) ? await TryLoadAsync(id, cancellationToken) : null;
            if (manifest is null || manifest.VideoRemoved is not null || (cutoff is { } before && manifest.CreatedAt >= before))
            {
                continue;
            }

            var files = VideoFiles(manifest, store.GetProjectFolder(id));
            if (files.Count > 0)
            {
                recordingsFound++;
                bytes += files.Sum(f => f.Bytes);
            }
        }

        return (recordingsFound, bytes);
    }

    /// <summary><c>video.remove</c>: deletes the recording's video files and returns their size.</summary>
    /// <exception cref="BridgeException"><c>project.notFound</c>, <c>project.recording</c>, <c>library.busy</c>, <c>video.nothingToRemove</c>.</exception>
    public async Task<long> RemoveAsync(string recordingId, string reason, CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            return await RemoveLockedAsync(recordingId, reason, cancellationToken);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Settings › Storage and history › Remove video older than: removes the video of every recording older than the
    /// age; busy ones wait for the next sweep. Returns how many recordings lost their video.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        if (settings.Current.Storage.RemoveVideoOlderThanDays is not { } days)
        {
            return 0;
        }

        // Never while a recording runs or processing works (the files may be in use, and the PC is busy).
        if (recordings.Current is not null || processing.IsProcessing)
        {
            return 0;
        }

        var cutoff = time.GetUtcNow() - TimeSpan.FromDays(days);
        var removed = 0;
        foreach (var id in store.ListIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await TryLoadAsync(id, cancellationToken);
            if (manifest is null || manifest.VideoRemoved is not null || manifest.VideoTracks.Count == 0 || manifest.CreatedAt >= cutoff
                || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
            {
                continue;
            }

            try
            {
                await RemoveAsync(id, ProjectVideoRemoved.ByAge, cancellationToken);
                removed++;
            }
            catch (BridgeException ex)
            {
                LogSweepSkipped(id, ex.Code);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
            {
                LogSweepFailed(ex, id);
            }
        }

        return removed;
    }

    private async Task<long> RemoveLockedAsync(string recordingId, string reason, CancellationToken cancellationToken)
    {
        ProjectManifest manifest;
        try
        {
            manifest = await store.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }

        if (recordings.IsBusy(recordingId) || manifest.State is ProjectStates.Recording or ProjectStates.Finalizing)
        {
            throw new BridgeException(
                DomainErrorCodes.ProjectRecording,
                $"\"{manifest.Details.Title}\" is still being recorded or saved, so its video can't be removed yet. Nothing was changed; try again once it is in the Library.",
                recordingId);
        }

        if (processing.RunningStage(recordingId) == StageNames.Video)
        {
            throw new BridgeException(
                DomainErrorCodes.LibraryBusy,
                $"The video of \"{manifest.Details.Title}\" is being checked right now. Nothing was changed; try again when its Video stage is done.",
                "video");
        }

        var folder = store.GetProjectFolder(recordingId);
        var files = VideoFiles(manifest, folder);
        if (manifest.VideoTracks.Count == 0 || manifest.VideoRemoved is not null || files.Count == 0)
        {
            throw new BridgeException(
                BridgeErrorCodes.InvalidParams,
                manifest.VideoRemoved is { } earlier
                    ? $"The video of \"{manifest.Details.Title}\" was already removed on {earlier.At:d}. Nothing was changed."
                    : $"\"{manifest.Details.Title}\" has no video files to remove. Nothing was changed.",
                recordingId);
        }

        var bytes = files.Sum(f => f.Bytes);
        var removedAt = time.GetLocalNow();
        var record = new ProjectVideoRemoved(removedAt, manifest.VideoTracks.Select(t => t.Id).ToList(), bytes, reason);
        var names = files.Select(f => f.File).ToHashSet(StringComparer.Ordinal);

        // The manifest says it first, so a crash between here and the deletes leaves a recording that knows its video is gone.
        await catalog.UpdateAsync(
            recordingId,
            m => m with
            {
                VideoRemoved = record,
                HasVideo = false,
                VideoTracks = m.VideoTracks.Select(t => t with { Sha256 = null }).ToList(),
                Integrity = m.Integrity with { Files = m.Integrity.Files.Where(kv => !names.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) },
                Stages = Processing.StageList.Without(m.Stages, StageNames.Video),
                Failures = Processing.StageList.WithFailure(m.Failures, null, StageNames.Video),
            },
            cancellationToken);

        foreach (var (file, _) in files)
        {
            await DeleteAsync(recordingId, ProjectPaths.Resolve(folder, file));
        }

        await store.AppendHistoryAsync(
            recordingId,
            new HistoryEntry(
                removedAt,
                StageNames.Video,
                "completed",
                $"Removed the video · {HumanFormat.Bytes(bytes)} freed",
                (reason == ProjectVideoRemoved.ByAge && settings.Current.Storage.RemoveVideoOlderThanDays is { } days
                    ? $"Settings › Storage removes video older than {HumanFormat.Count(days, "day", "days")}. "
                    : string.Empty)
                + $"{HumanFormat.Count(manifest.VideoTracks.Count, "video track", "video tracks")} removed. Audio tracks, the transcript and documents are kept; the video can't be brought back."),
            CancellationToken.None);
        await catalog.TouchedAsync(recordingId, CancellationToken.None);
        LogRemoved(recordingId, files.Count, bytes, reason);
        return bytes;
    }

    private async Task<ProjectManifest?> TryLoadAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(id, cancellationToken);
        }
        catch (Exception ex) when (ex is ProjectNotFoundException or ProjectSchemaException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Review may still be playing the video: a file in use is retried for a while; a leftover only costs space.</summary>
    private async Task DeleteAsync(string recordingId, string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= DeleteRetryDelays.Length)
                {
                    LogNotRemoved(ex, recordingId, Path.GetFileName(path));
                    return;
                }

                await Task.Delay(DeleteRetryDelays[attempt], time);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: removed {Files} video files ({Bytes} bytes, {Reason})")]
    private partial void LogRemoved(string recordingId, int files, long bytes, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: video file {File} could not be removed; it only costs space")]
    private partial void LogNotRemoved(Exception exception, string recordingId, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Remove video older than: recording {RecordingId} skipped ({Code}); the next sweep tries again")]
    private partial void LogSweepSkipped(string recordingId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remove video older than: recording {RecordingId} failed")]
    private partial void LogSweepFailed(Exception exception, string recordingId);
}
