using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Processing;

/// <summary>
/// The <c>mix</c> stage (after 2.0.0-beta.1): "Rebuild the mix with evened levels" (or "as recorded") from Review › More ›
/// Reprocess (<c>project.rebuildMix</c>). It never runs by itself: the request is written into <c>project.json</c>
/// (<see cref="ProjectManifest.MixRebuild"/>) and the stage queued, so closing Memento mid-way runs it again at the next
/// start. The stored tracks are mixed again by <see cref="ITrackFinalizer.RebuildMixAsync"/> into new files beside the
/// mix, which then replace <c>mix.flac</c> and <c>peaks.json</c>; the manifest records the new hash and the gains
/// (<see cref="ProjectMix.EvenLevels"/>), and History says what changed and that rebuilding again is the way back. The
/// tracks are only read. A mix that had been made smaller (AAC or MP3) is replaced by the lossless one and the
/// <c>optimize</c> stage is queued to make it smaller again when Settings still asks for that. Light: it waits for no gate.
/// </summary>
public sealed partial class MixStage(
    IProjectStore store,
    ProjectCatalog catalog,
    StageStatusWriter status,
    ITrackFinalizer finalizer,
    ISettingsStore settings,
    TimeProvider time,
    ILogger<MixStage> logger) : IProcessingStage
{
    /// <summary>The new files' name until they replace the mix.</summary>
    public const string RebuildStem = "mix.rebuilt";

    private readonly ILogger<MixStage> _logger = logger;

    public string Name => StageNames.Mix;

    /// <summary>After the transcript stages and the video, before <c>optimize</c> (which may make the new mix smaller).</summary>
    public int Order => 90;

    public bool IsHeavy => false;

    /// <summary>Never queued by itself after a recording: only on request.</summary>
    public bool AppliesTo(AppSettings settings) => false;

    public async Task RunAsync(StageRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var recordingId = run.RecordingId;
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        if (manifest.MixRebuild is not { } request || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
        {
            await status.RemoveAsync(recordingId, Name, cancellationToken);
            return;
        }

        var folder = store.GetProjectFolder(recordingId);
        var summary = request.EvenLevels ? "Rebuilding the mix with evened levels" : "Rebuilding the mix as recorded";
        await status.SetAsync(recordingId, new StageStatus(Name, StageStates.Active, 0, "Mixing"), cancellationToken);
        await AppendQuietlyAsync(recordingId, new HistoryEntry(time.GetLocalNow(), Name, "started", summary, null), cancellationToken);
        RebuiltMix rebuilt;
        try
        {
            if (manifest.MixOnly is not null)
            {
                throw new InvalidDataException("the separate track files were removed with Keep only the mix, so there is nothing to mix again");
            }

            var tracks = manifest.Tracks.Select(t => new MixRebuildTrack(t.Id, t.File, t.StartOffsetMs, t.EndedEarlyAtMs)).ToList();
            var progress = new Progress<int>(p => status.PublishProgress(manifest, new StageStatus(Name, StageStates.Active, p, string.Create(CultureInfo.InvariantCulture, $"Mixing {p}%"))));
            rebuilt = await finalizer.RebuildMixAsync(new MixRebuildRequest(folder, tracks, request.EvenLevels, RebuildStem), progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await status.SetAsync(recordingId, StageStatusWriter.QueuedStatus(Name), CancellationToken.None, clearFailure: false);
            throw;
        }
#pragma warning disable CA1031 // Any failure keeps the current mix and every track, and is reported with Retry.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await RecordFailureAsync(recordingId, ex, cancellationToken);
            return;
        }

        await SwapInAsync(recordingId, manifest, folder, request, rebuilt);
    }

    /// <summary>The new mix and peaks replace the old ones, then the manifest says so, then History.</summary>
    private async Task SwapInAsync(string recordingId, ProjectManifest manifest, string folder, ProjectMixRebuild request, RebuiltMix rebuilt)
    {
        var target = ProjectLayout.MixBaseName + Path.GetExtension(rebuilt.Mix.File);
        var peaks = manifest.Peaks ?? ProjectLayout.PeaksFile;
        var targetPath = ProjectPaths.Resolve(folder, target);
        ClearReadOnly(targetPath);
        File.Move(ProjectPaths.Resolve(folder, rebuilt.Mix.File), targetPath, overwrite: true);
        File.SetAttributes(targetPath, File.GetAttributes(targetPath) | FileAttributes.ReadOnly);
        File.Move(ProjectPaths.Resolve(folder, rebuilt.PeaksFile), ProjectPaths.Resolve(folder, peaks), overwrite: true);

        var at = time.GetLocalNow();
        var old = manifest.Mix?.File;
        var smaller = OptimizeStage.AppliesTo(settings.Current.Recording.Storage) && settings.Current.Recording.Storage.IsLossy;
        var queueOptimize = old is not null && !string.Equals(old, target, StringComparison.OrdinalIgnoreCase) && smaller;
        await catalog.UpdateAsync(
            recordingId,
            m =>
            {
                var files = new Dictionary<string, string>(m.Integrity.Files, StringComparer.Ordinal);
                if (m.Mix is { } previous)
                {
                    files.Remove(previous.File);
                }

                files[target] = rebuilt.Mix.Sha256;
                var stages = StageList.With(m.Stages, new StageStatus(Name, StageStates.Done, null, "Done"));
                if (queueOptimize)
                {
                    stages = StageList.With(stages, StageStatusWriter.QueuedStatus(StageNames.Optimize));
                }

                return m with
                {
                    Mix = new ProjectMix(target, rebuilt.Mix.Codec, rebuilt.Mix.SampleRate, rebuilt.Mix.Channels, rebuilt.Mix.DurationMs, rebuilt.Mix.Sha256)
                    {
                        EvenLevels = rebuilt.Gains is { } gains ? new ProjectMixLevels(at, gains) : null,
                    },
                    Peaks = peaks,
                    Integrity = m.Integrity with { ComputedAt = at, Files = files },
                    MixRebuild = null,
                    Stages = stages,
                    Failures = StageList.WithFailure(m.Failures, null, Name),
                };
            },
            CancellationToken.None);
        status.PublishProgress(await store.LoadAsync(recordingId, CancellationToken.None), new StageStatus(Name, StageStates.Done, null, "Done"));

        // A smaller mix (mix.mp3, mix.m4a) the lossless one replaced goes only now that the manifest points at the new one.
        if (old is not null && !string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
        {
            DeleteQuietly(recordingId, ProjectPaths.Resolve(folder, old));
        }

        var name = (string id) => manifest.Tracks.FirstOrDefault(t => t.Id == id)?.Name ?? id;
        var what = rebuilt.Gains is { } evened ? MixLevels.Describe(evened, name) : "every track summed as it was recorded";
        var again = request.EvenLevels
            ? "To undo it, choose Rebuild the mix as recorded in Review › More › Reprocess."
            : "To even the levels again, choose Rebuild the mix with evened levels in Review › More › Reprocess.";
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(
                at,
                Name,
                "completed",
                request.EvenLevels ? "Rebuilt the mix with evened levels" : "Rebuilt the mix as recorded",
                $"{what} · the tracks and the transcript were not changed{(queueOptimize ? " · the mix is made smaller again next" : string.Empty)}. {again}"),
            CancellationToken.None);
        await catalog.TouchedAsync(recordingId, CancellationToken.None);
        LogRebuilt(recordingId, request.EvenLevels);
    }

    private async Task RecordFailureAsync(string recordingId, Exception ex, CancellationToken cancellationToken)
    {
        LogFailed(ex, recordingId);
        var reason = ex is IOException or InvalidDataException or UnauthorizedAccessException ? ex.Message.TrimEnd('.') : "a track could not be read";
        var failure = new ProjectStageFailure(
            Name,
            $"Rebuilding the mix stopped: {reason}.",
            "The current mix and every track are unchanged.",
            [new Remedy(Remedies.Retry, "Try again")],
            ProjectStageFailure.CauseEngine,
            time.GetLocalNow());
        await status.FailAsync(recordingId, failure, "Mix failed", cancellationToken);
        await store.UpdateAsync(recordingId, m => m with { MixRebuild = null }, CancellationToken.None);
        await AppendQuietlyAsync(recordingId, new HistoryEntry(time.GetLocalNow(), Name, "failed", "Rebuilding the mix failed", failure.Message + " " + failure.Kept), CancellationToken.None);
    }

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    private void DeleteQuietly(string recordingId, string path)
    {
        try
        {
            ClearReadOnly(path);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogNotRemoved(ex, recordingId, Path.GetFileName(path));
        }
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: mix rebuilt (evened levels: {EvenLevels})")]
    private partial void LogRebuilt(string recordingId, bool evenLevels);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: rebuilding the mix failed; the current mix is kept")]
    private partial void LogFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: the replaced mix {File} could not be removed")]
    private partial void LogNotRemoved(Exception exception, string recordingId, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);
}
