using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Processing;

/// <summary>
/// "Keep only the mix" (Settings › Recording › Storage, 2.0; off by default): after every other stage, and after the
/// smaller format when one is chosen, the separate track files go and only the mix stays. The mix is first checked
/// against the SHA-256 in the manifest; if it is missing or does not match, nothing is removed. The manifest records
/// it (<see cref="ProjectManifest.MixOnly"/>) before any file is deleted, so a crash in between leaves only space to
/// reclaim, never a manifest that vouches for a file that is gone. Speakers cannot be identified per track again and
/// tracks cannot be exported for that recording; its transcript, speakers and mix are kept.
/// </summary>
public sealed partial class OptimizeStage
{
    public const string KeptTracksSummary = "Kept the separate tracks";

    /// <summary>Removes the separate track files of <paramref name="recordingId"/>; never throws for a file problem.</summary>
    public async Task KeepOnlyTheMixAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        if (manifest.MixOnly is not null)
        {
            await SetStageAsync(recordingId, StageStates.Done, null, "Done", cancellationToken);
            return;
        }

        var folder = store.GetProjectFolder(recordingId);
        if (manifest.Mix is not { } mix || !ProjectPaths.IsSafeRelative(mix.File) || !File.Exists(Full(folder, mix.File)))
        {
            await KeepTracksAsync(recordingId, $"The mix{(manifest.Mix is { } m ? $" ({Path.GetFileName(m.File)})" : string.Empty)} is missing, so the separate tracks stay. Nothing was removed.", cancellationToken);
            return;
        }

        await SetStageAsync(recordingId, StageStates.Active, null, "Keeping only the mix", cancellationToken);
        var sha256 = await FileHashes.Sha256Async(Full(folder, mix.File), cancellationToken);
        if (!string.Equals(sha256, mix.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            await KeepTracksAsync(recordingId, $"{Path.GetFileName(mix.File)} does not match the checksum saved when it was made, so the separate tracks stay. Nothing was removed.", cancellationToken);
            return;
        }

        var removed = new List<(ProjectTrack Track, IReadOnlyList<string> Files)>();
        foreach (var track in manifest.Tracks)
        {
            var files = TrackFiles(folder, track);
            if (files.Count > 0)
            {
                removed.Add((track, files));
            }
        }

        var bytes = removed.SelectMany(r => r.Files).Sum(f => SafeLength(Full(folder, f)));
        var at = time.GetLocalNow();
        var ids = removed.Select(r => r.Track.Id).ToHashSet(StringComparer.Ordinal);
        var paths = removed.SelectMany(r => r.Files).ToHashSet(StringComparer.Ordinal);

        // The manifest first: from here on it vouches only for the mix.
        await catalog.UpdateAsync(
            recordingId,
            m =>
            {
                var files = new Dictionary<string, string>(m.Integrity.Files, StringComparer.Ordinal);
                foreach (var path in paths)
                {
                    files.Remove(path);
                }

                return m with
                {
                    Tracks = m.Tracks.Select(t => ids.Contains(t.Id) ? t with { Sha256 = null } : t).ToList(),
                    Integrity = m.Integrity with { Files = files },
                    MixOnly = new ProjectMixOnly(at, [.. ids.Order(StringComparer.Ordinal)], bytes),
                    Stages = WithOptimize(m.Stages, new StageStatus(StageNames.Optimize, StageStates.Done, null, "Done")),
                };
            },
            CancellationToken.None);

        await DeleteFilesAsync(recordingId, folder, paths);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(
                at,
                StageNames.Optimize,
                "completed",
                $"Kept only the mix · removed {HumanFormat.Count(removed.Count, "separate track", "separate tracks")} ({HumanFormat.Bytes(bytes)})",
                string.Join(
                    " · ",
                    $"Settings › Recording › Keep only the mix: {string.Join(", ", paths.Select(Path.GetFileName))} removed after {Path.GetFileName(mix.File)} was checked against its SHA-256",
                    "speakers can no longer be identified per track and tracks can no longer be exported for this recording",
                    "the transcript, the speakers and the mix are kept")),
            CancellationToken.None);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, (await store.LoadAsync(recordingId, CancellationToken.None)).Stages));
        await catalog.TouchedAsync(recordingId, CancellationToken.None);
        LogKeptOnlyMix(recordingId, removed.Count, bytes);
    }

    /// <summary>
    /// The files of one track on disk: the stored file, or every RIFF part of a track kept as WAV. A name that is not a
    /// file inside the project folder (a hand-edited or copied-in manifest) is never touched.
    /// </summary>
    private static List<string> TrackFiles(string folder, ProjectTrack track)
    {
        if (!ProjectPaths.IsSafeRelative(track.File))
        {
            return [];
        }

        var candidates = track.Codec == PassThroughWavEncoder.WavCodec ? CaptureParts.Find(folder, track.File) : [track.File.Replace('\\', '/')];
        var files = new List<string>();
        foreach (var file in candidates)
        {
            try
            {
                if (File.Exists(Full(folder, file)))
                {
                    files.Add(file);
                }
            }
            catch (InvalidDataException)
            {
                // Outside the folder: not this recording's file.
            }
        }

        return files;
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private async Task KeepTracksAsync(string recordingId, string why, CancellationToken cancellationToken)
    {
        await SetStageAsync(recordingId, StageStates.Done, null, "Done", cancellationToken);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(time.GetLocalNow(), StageNames.Optimize, "info", KeptTracksSummary, why),
            cancellationToken);
        LogKeptTracks(recordingId);
    }

    /// <summary>Review may still be playing a track; a file in use is retried for a while, and a leftover only costs space.</summary>
    private async Task DeleteFilesAsync(string recordingId, string folder, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var path = Full(folder, file);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    ClearReadOnly(path);
                    File.Delete(path);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= DeleteRetryDelays.Length)
                    {
                        LogReplacedNotRemoved(ex, recordingId, file);
                        break;
                    }

                    await Task.Delay(DeleteRetryDelays[attempt], time);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: kept only the mix, removed {Tracks} track files ({Bytes} bytes)")]
    private partial void LogKeptOnlyMix(string recordingId, int tracks, long bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: the separate tracks were kept because the mix could not be checked")]
    private partial void LogKeptTracks(string recordingId);
}
