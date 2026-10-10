namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// One video track for Review (<see cref="Project.VideoTracks"/>): the file to play and where it sits on the timeline,
/// so the player can slave the video's <c>currentTime</c> to the mix: video time = mix time − <see cref="StartOffsetMs"/>.
/// </summary>
/// <param name="Role"><c>screen</c> or <c>camera</c>.</param>
/// <param name="SourceKind"><c>display</c>, <c>window</c> or <c>camera</c>.</param>
/// <param name="Url"><c>https://library.memento/&lt;id&gt;/tracks/screen.mp4</c> (range requests work, so seeking does); <c>null</c> once removed or while recording.</param>
/// <param name="StartOffsetMs">The track's first frame on the recording timeline (the file itself starts at 0).</param>
/// <param name="EndedEarlyAtMs">Set when the track stopped before the recording (window closed, camera unplugged, disk full).</param>
/// <param name="EndReason"><c>sourceLost</c>, <c>protectedContent</c>, <c>diskFull</c>, <c>error</c>, <c>disabled</c> or <c>interrupted</c>.</param>
public sealed record VideoTrackInfo(
    string Id,
    string Role,
    string SourceKind,
    string Name,
    string? Url,
    int Width,
    int Height,
    int FrameRate,
    string Encoder,
    bool Hardware,
    long StartOffsetMs,
    long DurationMs,
    long SizeBytes,
    long FramesWritten,
    long FramesDropped,
    long? EndedEarlyAtMs,
    string? EndReason,
    string? Sha256);
