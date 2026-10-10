using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Projects;

/// <summary>
/// One video track as <c>project.json</c> stores it in <c>videoTracks[]</c> (schema v4, ARCHITECTURE.md §5.x). Video
/// tracks are kept apart from the audio <c>tracks[]</c> on purpose: every 1.x reader mixes, transcribes and hashes
/// every entry of <c>tracks[]</c> as audio, while <c>videoTracks[]</c> has been reserved and preserved untouched since
/// schema v1, so a project recorded with 2.0 still opens in 1.x with its audio intact.
/// </summary>
public sealed record VideoTrack
{
    public const string KindVideo = "video";

    public const string RoleScreen = "screen";

    public const string RoleCamera = "camera";

    /// <summary>Unique in the project: <c>screen</c>, <c>camera</c>, <c>screen-2</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Always <c>video</c>, so a reader that walks every track of every list can tell them apart.</summary>
    public string Kind { get; init; } = KindVideo;

    /// <summary><c>screen</c> (a display or a window) or <c>camera</c>: what the picture-in-picture inset composes.</summary>
    public required string Role { get; init; }

    /// <summary><c>display</c>, <c>window</c> or <c>camera</c>.</summary>
    public required string SourceKind { get; init; }

    /// <summary><c>VideoSourceId</c> (Memento.Video) in its text form (<c>display:\\.\DISPLAY1</c>).</summary>
    public required string SourceId { get; init; }

    /// <summary>What the user saw: "Display 1", a window title, the camera's name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The file Review plays, relative to the project folder: the capture file (<c>tracks/screen.mp4</c>, fragmented)
    /// while recording, and after the Video stage the one joined file when the track had several parts.
    /// </summary>
    public required string File { get; init; }

    /// <summary>The files written while recording, in order; more than one when the encoder changed mid-recording.</summary>
    public IReadOnlyList<VideoTrackPart> Parts { get; init; } = [];

    public int Width { get; init; }

    public int Height { get; init; }

    public int FrameRate { get; init; }

    public string Codec { get; init; } = "h264";

    /// <summary><c>fmp4</c> (fragmented, as recorded) or <c>mp4</c> (after a remux).</summary>
    public string Container { get; init; } = "fmp4";

    /// <summary><c>smaller</c>, <c>balanced</c> or <c>best</c>.</summary>
    public string Quality { get; init; } = "balanced";

    public int BitrateBitsPerSecond { get; init; }

    /// <summary>The first part's encoder ("NVIDIA H.264 Encoder MFT").</summary>
    public string Encoder { get; init; } = string.Empty;

    public bool HardwareEncoder { get; init; }

    /// <summary>Where the first frame sits on the recording timeline (pauses excluded, like the audio tracks).</summary>
    public long StartOffsetMs { get; init; }

    public long DurationMs { get; init; }

    public long FramesWritten { get; init; }

    /// <summary>Frame slots skipped because capture or encoding fell behind (each one lengthens the frame before it).</summary>
    public long FramesDropped { get; init; }

    public long? EndedEarlyAtMs { get; init; }

    /// <summary><c>sourceLost</c>, <c>protectedContent</c>, <c>diskFull</c>, <c>error</c> or <c>disabled</c> when the track ended early.</summary>
    public string? EndReason { get; init; }

    public string? Sha256 { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
