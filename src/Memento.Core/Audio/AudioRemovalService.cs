using System.Text.RegularExpressions;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Audio;

/// <summary>
/// "Delete audio" (2.1, DESIGN.md §6.2): deletes a recording's audio (every track file, the mix and the waveform peaks)
/// after the page's confirmation (<c>audio.remove</c>) or, when Settings › Storage and history › Delete audio tracks older
/// than is set, for recordings older than that (<see cref="SweepAsync"/>). The same order as Remove video: the manifest
/// records it first (<c>audioRemoved</c>; the tracks stay listed without a hash, the mix and peaks are cleared), then the
/// files go, then History says what was freed. The transcript, documents, attachments and video are never touched, so
/// the recording stays readable. Never runs while the recording is recorded or saved, or while any of its stages is
/// queued or running (they read the audio).
/// </summary>
public sealed partial class AudioRemovalService(
    IProjectStore store,
    ProjectCatalog catalog,
    RecordingCoordinator recordings,
    ProcessingOrchestrator processing,
    ISettingsStore settings,
    TimeProvider time,
    ILogger<AudioRemovalService> logger) : IDisposable
{
    /// <summary>Settings › Storage and history › Delete audio tracks older than: exactly these ages (a day … five years); <c>null</c> is Never.</summary>
    public static IReadOnlyList<int> AgeChoices { get; } = [1, 7, 30, 90, 365, 730, 1095, 1460, 1825];

    private static readonly TimeSpan[] DeleteRetryDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly ILogger<AudioRemovalService> _logger = logger;

    public void Dispose() => _oneAtATime.Dispose();

    /// <summary>"a day", "a week", "a month", "90 days", "a year", "2 years" … the way Settings names an age.</summary>
    public static string AgeName(int days) => days switch
    {
        1 => "a day",
        7 => "a week",
        30 => "a month",
        365 => "a year",
        _ when days % 365 == 0 => HumanFormat.Count(days / 365, "year", "years"),
        _ => HumanFormat.Count(days, "day", "days"),
    };

    /// <summary>
    /// The audio files of <paramref name="manifest"/> on disk, relative to its folder, with their sizes: the files its
    /// tracks and mix name (only audio files directly in <c>tracks/</c>, and the mix and peaks at the top of the folder,
    /// so a crafted manifest can't point the delete at the transcript or a document), and the audio files a crash or an
    /// interrupted conversion left in <c>tracks/</c>. Video files (<c>.mp4</c>) are never among them.
    /// </summary>
    public static IReadOnlyList<(string File, long Bytes)> AudioFiles(ProjectManifest manifest, string folder)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var names = manifest.Tracks.SelectMany(t => new[] { t.File, t.CaptureFile }).OfType<string>().Where(IsTrackFile).ToList();
        if (manifest.Mix is { } mix && IsTopFile(mix.File))
        {
            names.Add(mix.File);
        }

        if (manifest.Peaks is { } peaks && IsTopFile(peaks))
        {
            names.Add(peaks);
        }

        var tracks = Path.Combine(folder, ProjectLayout.TracksFolder);
        if (manifest.Tracks.Count > 0 && Directory.Exists(tracks))
        {
            names.AddRange(Directory.EnumerateFiles(tracks)
                .Select(Path.GetFileName)
                .Where(name => name is not null && LeftoverTrackPattern().IsMatch(name))
                .Select(name => ProjectLayout.TracksFolder + "/" + name));
        }

        if (Directory.Exists(folder))
        {
            names.AddRange(Directory.EnumerateFiles(folder)
                .Select(Path.GetFileName)
                .Where(name => name is not null && TopAudioPattern().IsMatch(name))
                .Select(name => name!));
        }

        var result = new List<(string, long)>();
        foreach (var file in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = ProjectPaths.Resolve(folder, file);
            if (File.Exists(path))
            {
                result.Add((file, new FileInfo(path).Length));
            }
        }

        return result;
    }

    /// <summary>
    /// <c>audio.removeEstimate</c>: what Delete audio would free, for <paramref name="recordingIds"/>, or (null) every
    /// recording with audio older than <paramref name="olderThanDays"/> (null: every one).
    /// </summary>
    public async Task<(int Recordings, long Bytes)> EstimateAsync(IReadOnlyList<string>? recordingIds, int? olderThanDays, CancellationToken cancellationToken)
    {
        DateTimeOffset? cutoff = recordingIds is null && olderThanDays is { } days ? time.GetUtcNow() - TimeSpan.FromDays(days) : null;
        var ids = recordingIds?.Distinct(StringComparer.Ordinal).ToList() ?? (IReadOnlyList<string>)store.ListIds();
        var found = 0;
        long bytes = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = ProjectId.IsValid(id) ? await TryLoadAsync(id, cancellationToken) : null;
            if (manifest is null || manifest.AudioRemoved is not null || (cutoff is { } before && manifest.CreatedAt >= before)
                || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
            {
                continue;
            }

            var files = AudioFiles(manifest, store.GetProjectFolder(id));
            if (files.Count > 0)
            {
                found++;
                bytes += files.Sum(f => f.Bytes);
            }
        }

        return (found, bytes);
    }

    /// <summary><c>audio.remove</c>: deletes the recording's audio files and returns their size.</summary>
    /// <exception cref="BridgeException"><c>project.notFound</c>, <c>project.recording</c>, <c>library.busy</c>, <c>bridge.invalidParams</c> (nothing to delete).</exception>
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
    /// Settings › Storage and history › Delete audio tracks older than: deletes the audio of every recording older than
    /// the age; busy ones wait for the next sweep. Returns how many recordings lost their audio.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        if (settings.Current.Storage.DeleteAudioOlderThanDays is not { } days)
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
            if (manifest is null || manifest.AudioRemoved is not null || manifest.CreatedAt >= cutoff
                || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered)
                || AudioFiles(manifest, store.GetProjectFolder(id)).Count == 0)
            {
                continue;
            }

            try
            {
                await RemoveAsync(id, ProjectAudioRemoved.ByAge, cancellationToken);
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

        var title = manifest.Details.Title;
        if (recordings.IsBusy(recordingId) || manifest.State is ProjectStates.Recording or ProjectStates.Finalizing)
        {
            throw new BridgeException(
                DomainErrorCodes.ProjectRecording,
                $"\"{title}\" is still being recorded or saved, so its audio can't be deleted yet. Nothing was changed; try again once it is in the Library.",
                recordingId);
        }

        if (processing.IsBusy(recordingId) || manifest.Stages.Any(s => s.State is StageStates.Active or StageStates.Queued))
        {
            throw new BridgeException(
                DomainErrorCodes.LibraryBusy,
                $"\"{title}\" is being processed, and processing reads its audio. Nothing was changed; try again when it is done.",
                "audio");
        }

        var folder = store.GetProjectFolder(recordingId);
        var files = AudioFiles(manifest, folder);
        if (manifest.AudioRemoved is { } earlier)
        {
            throw new BridgeException(
                BridgeErrorCodes.InvalidParams,
                $"The audio of \"{title}\" was already deleted on {DateFormats.Format(earlier.At, settings.Current.General.DateFormat)}. Nothing was changed.",
                recordingId);
        }

        if (files.Count == 0)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"\"{title}\" has no audio files to delete. Nothing was changed.", recordingId);
        }

        var bytes = files.Sum(f => f.Bytes);
        var removedAt = time.GetLocalNow();
        var record = new ProjectAudioRemoved(removedAt, manifest.Tracks.Select(t => t.Id).ToList(), bytes, reason);
        var names = files.Select(f => f.File).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The manifest says it first, so a crash between here and the deletes leaves a recording that knows its audio is
        // gone (Review and Export stop offering it) rather than one that points at missing files.
        await catalog.UpdateAsync(
            recordingId,
            m => m with
            {
                AudioRemoved = record,
                Mix = null,
                Peaks = null,
                MixRebuild = null,
                Processing = null,
                Tracks = m.Tracks.Select(t => t with { Sha256 = null }).ToList(),
                Integrity = m.Integrity with { Files = m.Integrity.Files.Where(kv => !names.Contains(kv.Key) && !IsAudioIntegrityKey(m, kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) },
                Stages = StageList.Without(StageList.Without(m.Stages, StageNames.Optimize), StageNames.Mix),
                Failures = StageList.WithFailure(StageList.WithFailure(m.Failures, null, StageNames.Optimize), null, StageNames.Mix),
            },
            cancellationToken);

        foreach (var (file, _) in files)
        {
            await DeleteAsync(recordingId, ProjectPaths.Resolve(folder, file));
        }

        var hasVideo = manifest.VideoTracks.Count > 0 && manifest.VideoRemoved is null;
        await store.AppendHistoryAsync(
            recordingId,
            new HistoryEntry(
                removedAt,
                StageNames.Stored,
                "completed",
                $"Deleted the audio · {HumanFormat.Bytes(bytes)} freed",
                (reason == ProjectAudioRemoved.ByAge && settings.Current.Storage.DeleteAudioOlderThanDays is { } days
                    ? $"Settings › Storage deletes audio tracks older than {AgeName(days)}. "
                    : string.Empty)
                + $"{HumanFormat.Count(manifest.Tracks.Count, "audio track", "audio tracks")} and the mix removed. The transcript{(hasVideo ? ", the video" : string.Empty)} and documents are kept; the audio can't be brought back."),
            CancellationToken.None);
        await catalog.TouchedAsync(recordingId, CancellationToken.None);
        LogRemoved(recordingId, files.Count, bytes, reason);
        return bytes;
    }

    /// <summary>The track and mix hashes the manifest kept for audio files, even ones already missing on disk.</summary>
    private static bool IsAudioIntegrityKey(ProjectManifest manifest, string key) =>
        manifest.Tracks.Any(t => string.Equals(t.File, key, StringComparison.OrdinalIgnoreCase))
        || (manifest.Mix is { } mix && string.Equals(mix.File, key, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Review may still be playing the mix: a file in use is retried for a while; a leftover only costs space.</summary>
    private async Task DeleteAsync(string recordingId, string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Finalized tracks and the mix are marked read-only (only the Delete flows remove them).
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(path);
                return;
            }
            catch (FileNotFoundException)
            {
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: deleted {Files} audio files ({Bytes} bytes, {Reason})")]
    private partial void LogRemoved(string recordingId, int files, long bytes, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: audio file {File} could not be deleted; it only costs space")]
    private partial void LogNotRemoved(Exception exception, string recordingId, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delete audio older than: recording {RecordingId} skipped ({Code}); the next sweep tries again")]
    private partial void LogSweepSkipped(string recordingId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delete audio older than: recording {RecordingId} failed")]
    private partial void LogSweepFailed(Exception exception, string recordingId);

    /// <summary>An audio file directly in <c>tracks/</c>: what a track is made of (never a video <c>.mp4</c>).</summary>
    internal static bool IsTrackFile(string file) =>
        ProjectPaths.IsSafeRelative(file)
        && file.StartsWith(ProjectLayout.TracksFolder + "/", StringComparison.Ordinal)
        && file.IndexOf('/', ProjectLayout.TracksFolder.Length + 1) < 0
        && LeftoverTrackPattern().IsMatch(file[(ProjectLayout.TracksFolder.Length + 1)..]);

    /// <summary>The mix or the peaks at the top of the folder (<c>mix.flac</c>, <c>mix.m4a</c>, <c>peaks.json</c>).</summary>
    internal static bool IsTopFile(string file) => ProjectPaths.IsSafeRelative(file) && !file.Contains('/', StringComparison.Ordinal) && TopAudioPattern().IsMatch(file);

    /// <summary>Audio files the recorder, finalize and the optimize stage write in <c>tracks/</c> (<c>mic.flac</c>, <c>system.part2.wav</c>, <c>mic.m4a.tmp</c>).</summary>
    [GeneratedRegex(@"^[\w.-]{1,120}\.(wav|flac|m4a|aac|mp3)(\.tmp)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LeftoverTrackPattern();

    /// <summary>The mix, a rebuilt mix and the waveform peaks at the top of the folder.</summary>
    [GeneratedRegex(@"^(mix(\.rebuilt)?\.(wav|flac|m4a|aac|mp3)|(mix\.rebuilt\.)?peaks\.json)(\.tmp)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TopAudioPattern();
}
