using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Projects;

namespace Memento.Core.Video;

/// <summary>Turns a manifest's video tracks into what the Library rows and Review need (DESIGN.md §18.3, §18.7).</summary>
public static class VideoMapper
{
    public const string Present = "present";
    public const string Removed = "removed";

    /// <summary>The recording's video at a glance, or <c>null</c> for an audio recording.</summary>
    public static VideoSummary? Summarize(ProjectManifest manifest, string folder)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var tracks = manifest.VideoTracks;
        if (tracks.Count == 0)
        {
            return null;
        }

        var screen = tracks.Any(t => t.Role == VideoTrack.RoleScreen);
        var camera = tracks.Any(t => t.Role == VideoTrack.RoleCamera);
        var main = tracks.FirstOrDefault(t => t.Role == VideoTrack.RoleScreen) ?? tracks[0];
        var removed = manifest.VideoRemoved;
        return new VideoSummary(
            removed is null ? Present : Removed,
            screen && camera ? "screenAndCamera" : camera ? "camera" : "screen",
            main.Width,
            main.Height,
            main.FrameRate,
            tracks.Max(t => t.DurationMs),
            removed is null ? tracks.Sum(t => VideoStage.SizeOf(folder, t)) : 0,
            removed?.At,
            removed?.BytesFreed);
    }

    /// <summary>One track for Review; <see cref="VideoTrackInfo.Url"/> only once the recording is stored and while its file exists.</summary>
    public static VideoTrackInfo ToInfo(string recordingId, VideoTrack track, string folder, bool playable)
    {
        ArgumentNullException.ThrowIfNull(track);
        var file = ProjectPaths.IsSafeRelative(track.File) ? ProjectPaths.Resolve(folder, track.File) : null;
        var url = playable && file is not null && File.Exists(file) ? LibraryUrls.ForProjectFile(recordingId, track.File) : null;
        return new VideoTrackInfo(
            track.Id,
            track.Role,
            track.SourceKind,
            track.Name,
            url,
            track.Width,
            track.Height,
            track.FrameRate,
            track.Encoder,
            track.HardwareEncoder,
            track.StartOffsetMs,
            track.DurationMs,
            VideoStage.SizeOf(folder, track),
            track.FramesWritten,
            track.FramesDropped,
            track.EndedEarlyAtMs,
            track.EndReason,
            track.Sha256);
    }

    /// <summary>The inset as it was last set while recording, or <c>null</c> without a camera track.</summary>
    public static PipState? Pip(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.VideoTracks.All(t => t.Role != VideoTrack.RoleCamera))
        {
            return null;
        }

        return manifest.VideoPip.Count > 0
            ? new PipState(manifest.VideoPip[^1].Enabled, manifest.VideoPip[^1].Corner)
            : new PipState(true, VideoPipChange.BottomRight);
    }

    /// <summary>The summary as the library index keeps it (one JSON column).</summary>
    public static string? ToJson(VideoSummary? summary) =>
        summary is null ? null : JsonSerializer.Serialize(summary, VideoBridgeJsonContext.Default.VideoSummary);

    public static VideoSummary? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, VideoBridgeJsonContext.Default.VideoSummary);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
