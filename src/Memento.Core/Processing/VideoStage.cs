using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Video;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Processing;

/// <summary>
/// The <c>video</c> stage (2.0, DESIGN.md §18.7, ARCHITECTURE.md §5.x): for a recording with video, joins each track's
/// parts into one file when the encoder changed mid-recording (<c>tracks/&lt;id&gt;.part2.mp4</c>; copied without decoding,
/// or encoded again when the parts came from different encoders), computes the SHA-256 of every video file, and writes
/// them into the manifest and History. A clean stop needs nothing else: the fragmented MP4 plays and seeks as written.
/// It is light (no stage waits for it, and it waits for no gate) and runs after the transcript stages so it never holds
/// a transcription back. A failure keeps every recorded file unchanged.
/// </summary>
public sealed partial class VideoStage(
    IProjectStore store,
    ProjectCatalog catalog,
    StageStatusWriter status,
    IVideoFiles files,
    TimeProvider time,
    ILogger<VideoStage> logger) : IProcessingStage
{
    private static readonly TimeSpan[] DeleteRetryDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    private readonly ILogger<VideoStage> _logger = logger;

    public string Name => StageNames.Video;

    /// <summary>After transcript, speakers and topics, before optimize: the video never holds a transcription back.</summary>
    public int Order => 40;

    public bool IsHeavy => false;

    public bool AppliesTo(AppSettings settings) => true;

    /// <summary>Only recordings that have video files.</summary>
    public bool AppliesTo(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.VideoTracks.Count > 0 && manifest.VideoRemoved is null;
    }

    public async Task RunAsync(StageRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var recordingId = run.RecordingId;
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        if (!AppliesTo(manifest) || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
        {
            await status.RemoveAsync(recordingId, Name, cancellationToken);
            return;
        }

        var folder = store.GetProjectFolder(recordingId);
        var request = manifest.Processing ?? new ProcessingRequest();
        var lastPercent = 0;
        await status.SetAsync(recordingId, new StageStatus(Name, StageStates.Active, 0, "Checking"), cancellationToken);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(time.GetLocalNow(), Name, "started", "Checking the video", Describe(manifest.VideoTracks, folder)),
            cancellationToken);

        var done = new List<VideoTrack>();
        var joinedParts = new List<string>();
        var created = new List<string>();
        var notes = new List<string>();
        var partHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var count = manifest.VideoTracks.Count;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var track = manifest.VideoTracks[i];
                var share = 100.0 / count;
                var start = i * share;
                if (NeedsJoin(track, folder) && request.VideoKeepRaw)
                {
                    // "Keep raw frames": the parts stay as written; each is hashed and the stage is done.
                    foreach (var part in track.Parts.Skip(1))
                    {
                        partHashes[part.File] = await FileHashes.Sha256Async(ProjectPaths.Resolve(folder, part.File), cancellationToken);
                    }

                    notes.Add($"kept the {track.Role} video's {track.Parts.Count} parts as they were written");
                }
                else if (NeedsJoin(track, folder))
                {
                    var (joined, parts, note) = await JoinAsync(recordingId, manifest, folder, track, start, share * 0.8, request.VideoSoftware, p => lastPercent = p, cancellationToken);
                    track = joined;
                    created.Add(joined.File);
                    joinedParts.AddRange(parts);
                    notes.Add(note);
                }

                lastPercent = (int)(start + (share * 0.8));
                status.PublishProgress(manifest, new StageStatus(Name, StageStates.Active, lastPercent, "Checking"));
                var path = ProjectPaths.Resolve(folder, track.File);
                var sha256 = File.Exists(path) ? await FileHashes.Sha256Async(path, cancellationToken) : null;
                done.Add(track with { Sha256 = sha256 });
            }
        }
        catch (OperationCanceledException)
        {
            RemoveQuietly(recordingId, folder, created);
            await status.SetAsync(recordingId, StageStatusWriter.QueuedStatus(Name), CancellationToken.None, clearFailure: false);
            throw;
        }
#pragma warning disable CA1031 // Any failure keeps every recorded file and is reported as a failed stage with Retry.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RemoveQuietly(recordingId, folder, created);
            await RecordFailureAsync(recordingId, ex, lastPercent, cancellationToken);
            return;
        }

        var computedAt = time.GetLocalNow();
        var byId = done.ToDictionary(t => t.Id, StringComparer.Ordinal);
        await catalog.UpdateAsync(
            recordingId,
            m =>
            {
                var hashes = new Dictionary<string, string>(m.Integrity.Files, StringComparer.Ordinal);
                foreach (var track in m.VideoTracks)
                {
                    foreach (var part in track.Parts)
                    {
                        hashes.Remove(part.File);
                    }
                }

                foreach (var track in done.Where(t => t.Sha256 is not null))
                {
                    hashes[track.File] = track.Sha256!;
                }

                foreach (var (file, sha256) in partHashes)
                {
                    hashes[file] = sha256;
                }

                return m with
                {
                    VideoTracks = m.VideoTracks.Select(t => byId.TryGetValue(t.Id, out var checkedTrack) ? checkedTrack : t).ToList(),
                    Integrity = m.Integrity with { ComputedAt = computedAt, Files = hashes },
                    Stages = StageList.With(m.Stages, new StageStatus(Name, StageStates.Done, null, "Done")),
                    Failures = StageList.WithFailure(m.Failures, null, Name),
                    Processing = m.Processing is { } p ? p with { VideoSoftware = false, VideoKeepRaw = false } : null,
                };
            },
            cancellationToken);
        status.PublishProgress(await store.LoadAsync(recordingId, cancellationToken), new StageStatus(Name, StageStates.Done, null, "Done"));

        // The joined file is in the manifest with its hash; only now do the parts it replaced go.
        await DeleteAsync(recordingId, folder, joinedParts);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(time.GetLocalNow(), Name, "completed", "Video checked", Describe(done, folder) + string.Concat(notes.Select(n => " · " + n))),
            CancellationToken.None);
        LogDone(recordingId, done.Count, joinedParts.Count);
    }

    /// <summary>"screen: Display 1 · 2560 × 1440 · 30 fps · 20:31 · 1.9 GB · NVIDIA H.264 Encoder MFT (hardware)".</summary>
    internal static string Describe(IReadOnlyList<VideoTrack> tracks, string folder) =>
        string.Join(
            "; ",
            tracks.Select(t =>
                string.Create(CultureInfo.InvariantCulture, $"{t.Role}: {t.Name} · {t.Width} × {t.Height} · {t.FrameRate} fps · {HumanFormat.Clock(t.DurationMs)} · {HumanFormat.Bytes(SizeOf(folder, t))} · {t.Encoder} ({(t.HardwareEncoder ? "hardware" : "software")})")
                + (t.FramesDropped > 0 ? $" · {HumanFormat.Count((int)Math.Min(int.MaxValue, t.FramesDropped), "dropped frame", "dropped frames")}" : string.Empty)
                + (t.EndedEarlyAtMs is { } ended && t.EndReason is not ("disabled" or null) ? $" · ended at {HumanFormat.Clock(ended)}" : string.Empty)));

    /// <summary>Every file of the track on disk (the joined file, or its parts), in bytes.</summary>
    internal static long SizeOf(string folder, VideoTrack track)
    {
        long total = 0;
        foreach (var file in track.Parts.Select(p => p.File).Append(track.File).Distinct(StringComparer.Ordinal))
        {
            if (!ProjectPaths.IsSafeRelative(file))
            {
                continue;
            }

            var path = ProjectPaths.Resolve(folder, file);
            total += File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        return total;
    }

    private static bool NeedsJoin(VideoTrack track, string folder) =>
        track.Parts.Count > 1
        && track.Parts.Any(p => p.File == track.File)
        && track.Parts.All(p => File.Exists(ProjectPaths.Resolve(folder, p.File)));

    private async Task<(VideoTrack Track, IReadOnlyList<string> Parts, string Note)> JoinAsync(
        string recordingId,
        ProjectManifest manifest,
        string folder,
        VideoTrack track,
        double start,
        double share,
        bool software,
        Action<int> reached,
        CancellationToken cancellationToken)
    {
        var output = $"tracks/{track.Id}.joined.mp4";
        var first = track.Parts[0].StartOffsetMs;
        var parts = track.Parts.Select(p => new VideoJoinPart(ProjectPaths.Resolve(folder, p.File), p.StartOffsetMs - first)).ToList();
        var progress = new Progress<double>(fraction =>
        {
            var percent = (int)(start + (share * Math.Clamp(fraction, 0, 1)));
            reached(percent);
            status.PublishProgress(manifest, new StageStatus(Name, StageStates.Active, percent, software ? string.Create(CultureInfo.InvariantCulture, $"Encoding {percent}% · CPU") : string.Create(CultureInfo.InvariantCulture, $"Joining {percent}%")));
        });
        var result = await files.JoinAsync(parts, ProjectPaths.Resolve(folder, output), software, progress, cancellationToken);
        LogJoined(recordingId, track.Id, track.Parts.Count, result.Reencoded);
        var note = $"joined the {track.Role} video's {track.Parts.Count} parts into one file" + (result.Reencoded ? " (encoded again: the encoder changed while recording)" : string.Empty);
        return (track with { File = output, Container = "mp4", DurationMs = Math.Max(track.DurationMs, result.DurationMs) }, track.Parts.Select(p => p.File).ToList(), note);
    }

    private async Task RecordFailureAsync(string recordingId, Exception ex, int percent, CancellationToken cancellationToken)
    {
        LogFailed(ex, recordingId);
        var reason = ex is VideoStartException or IOException or UnauthorizedAccessException ? ex.Message.TrimEnd('.') : "Media Foundation could not read a video file";
        var failure = new ProjectStageFailure(
            Name,
            string.Create(CultureInfo.InvariantCulture, $"Finalising the video file stopped at {percent}%: {reason}."),
            "The raw frames and all audio tracks are safe, and the transcript was not affected.",
            [new Remedy(Remedies.Software, "Retry with software encoder"), new Remedy(Remedies.KeepRaw, "Keep raw frames")],
            ProjectStageFailure.CauseEngine,
            time.GetLocalNow());
        await status.FailAsync(recordingId, failure, "Video failed", cancellationToken);
        await AppendQuietlyAsync(recordingId, new HistoryEntry(time.GetLocalNow(), Name, "failed", "Video failed", failure.Message + " " + failure.Kept), cancellationToken);
    }

    private async Task DeleteAsync(string recordingId, string folder, IEnumerable<string> relativeFiles)
    {
        foreach (var file in relativeFiles)
        {
            var path = ProjectPaths.Resolve(folder, file);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Delete(path);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= DeleteRetryDelays.Length)
                    {
                        LogNotRemoved(ex, recordingId, file);
                        break;
                    }

                    await Task.Delay(DeleteRetryDelays[attempt], time);
                }
            }
        }
    }

    private void RemoveQuietly(string recordingId, string folder, IEnumerable<string> relativeFiles)
    {
        foreach (var file in relativeFiles)
        {
            try
            {
                File.Delete(ProjectPaths.Resolve(folder, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogNotRemoved(ex, recordingId, file);
            }
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
            LogNotRemoved(ex, recordingId, "history.jsonl");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: video checked ({Tracks} tracks, {Parts} parts joined)")]
    private partial void LogDone(string recordingId, int tracks, int parts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: video track {TrackId} joined from {Parts} parts (encoded again: {Reencoded})")]
    private partial void LogJoined(string recordingId, string trackId, int parts, bool reencoded);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: the video stage failed; every recorded file is kept")]
    private partial void LogFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: {File} could not be removed or written")]
    private partial void LogNotRemoved(Exception exception, string recordingId, string file);
}
