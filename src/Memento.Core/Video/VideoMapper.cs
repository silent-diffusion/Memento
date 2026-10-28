using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Serialization;

namespace Memento.Core.Video;

/// <summary>Turns a manifest's video tracks into what the Library rows and Review need (DESIGN.md §18.3, §18.7).</summary>
public static class VideoMapper
{
    /// <summary>The Library's summary, or <c>null</c> when the recording never had video.</summary>
    public static RecordingVideoSummary? Summarize(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var tracks = manifest.VideoTracks;
        if (tracks.Count == 0)
        {
            return null;
        }

        var main = tracks.FirstOrDefault(t => t.Role == VideoTrack.RoleScreen) ?? tracks[0];
        return new RecordingVideoSummary(Roles(tracks), main.Height, manifest.VideoRemoved?.At);
    }

    /// <summary>Review's video, or <c>null</c> when the recording never had video.</summary>
    /// <param name="playable">The recording is stored, so its files can be played (URLs are given only then).</param>
    public static ProjectVideo? ToProject(ProjectManifest manifest, string folder, bool playable)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.VideoTracks.Count == 0)
        {
            return null;
        }

        var removed = manifest.VideoRemoved;
        var tracks = removed is not null
            ? []
            : manifest.VideoTracks
                .OrderBy(t => t.Role == VideoTrack.RoleScreen ? 0 : 1)
                .ThenBy(t => t.StartOffsetMs)
                .Select(t => ToTrack(manifest.Id, t, folder, playable))
                .ToList();
        return new ProjectVideo(
            tracks,
            Pip(manifest),
            removed is null ? manifest.VideoTracks.Sum(t => VideoStage.SizeOf(folder, t)) : 0,
            removed is null ? null : new VideoRemovedInfo(removed.At, removed.BytesFreed));
    }

    public static ProjectVideoTrack ToTrack(string recordingId, VideoTrack track, string folder, bool playable)
    {
        ArgumentNullException.ThrowIfNull(track);

        // After the video stage joined the parts, the joined file is the one part to play.
        var files = track.Parts.Any(p => p.File == track.File) || track.Parts.Count == 0
            ? track.Parts.Select(p => (p.File, p.StartOffsetMs, p.DurationMs)).ToList()
            : [(track.File, track.StartOffsetMs, track.DurationMs)];
        if (files.Count == 0)
        {
            files.Add((track.File, track.StartOffsetMs, track.DurationMs));
        }

        var parts = files.Select(f => new VideoPart(Url(recordingId, folder, f.File, playable), f.StartOffsetMs, f.DurationMs)).ToList();
        return new ProjectVideoTrack(
            track.Role,
            track.SourceKind,
            track.Name,
            track.Width,
            track.Height,
            track.FrameRate,
            track.Encoder,
            track.HardwareEncoder,
            track.FramesWritten,
            track.FramesDropped,
            VideoStage.SizeOf(folder, track),
            track.StartOffsetMs,
            track.DurationMs,
            EndSentence(track),
            parts,
            track.Id,
            StoppedAt(track));
    }

    /// <summary>2.8.0: where the track stopped on the recording timeline (schema v7), or start + duration for a track not yet stopped.</summary>
    public static long StoppedAt(VideoTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.StoppedAtMs ?? track.EndedEarlyAtMs ?? Math.Max(track.StartOffsetMs, track.StartOffsetMs + track.DurationMs);
    }

    /// <summary>The §18.6 sentence for a track that ended before the recording did; <c>null</c> otherwise (also when turned off).</summary>
    public static string? EndSentence(VideoTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.EndedEarlyAtMs is not { } at || track.EndReason is null or "disabled")
        {
            return null;
        }

        var when = HumanFormat.Clock(at);
        return (track.EndReason, track.SourceKind) switch
        {
            ("sourceLost", "window") => $"“{track.Name}” was closed at {when}.",
            ("sourceLost", "display") => $"{track.Name} was disconnected at {when}.",
            ("sourceLost", _) => $"{track.Name} was disconnected at {when}.",
            ("protectedContent", _) => $"“{track.Name}” started showing protected content at {when}; the video stopped there.",
            ("diskFull", _) => $"Video stopped at {when} because the drive was nearly full; audio kept recording.",
            ("interrupted", _) => $"The video ends at {when}, where Memento stopped unexpectedly.",
            _ => $"The video could not be written after {when}; everything before it is kept.",
        };
    }

    /// <summary>The inset as it was last set while recording.</summary>
    public static PipState Pip(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.VideoPip.Count > 0
            ? new PipState(manifest.VideoPip[^1].Enabled, manifest.VideoPip[^1].Corner)
            : new PipState(manifest.VideoTracks.Any(t => t.Role == VideoTrack.RoleCamera) && manifest.VideoTracks.Any(t => t.Role == VideoTrack.RoleScreen), VideoPipChange.BottomRight);
    }

    /// <summary>The summary as the library index keeps it (one JSON column).</summary>
    public static string? ToJson(RecordingVideoSummary? summary) =>
        summary is null ? null : JsonSerializer.Serialize(summary, VideoBridgeJsonContext.Default.RecordingVideoSummary);

    public static RecordingVideoSummary? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            // The library index's cached copy: one with a null that cannot be repaired is shown as no video summary.
            var summary = JsonSerializer.Deserialize(json, VideoBridgeJsonContext.Default.RecordingVideoSummary);
            return summary is null || NullMembers.Repair(summary).Unrepaired.Count > 0 ? null : summary;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> Roles(IReadOnlyList<VideoTrack> tracks) =>
        new[] { VideoTrack.RoleScreen, VideoTrack.RoleCamera }.Where(r => tracks.Any(t => t.Role == r)).ToList();

    private static string? Url(string recordingId, string folder, string file, bool playable)
    {
        if (!playable || !ProjectPaths.IsSafeRelative(file))
        {
            return null;
        }

        return File.Exists(ProjectPaths.Resolve(folder, file)) ? LibraryUrls.ForProjectFile(recordingId, file) : null;
    }
}
