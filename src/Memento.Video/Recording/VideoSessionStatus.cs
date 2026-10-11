namespace Memento.Video.Recording;

/// <summary>
/// What the Recording screen's Encoder card and footer show for one video track (DESIGN.md §18.1): encoder kind,
/// resolution, frame rate with dropped frames, bitrate, size so far.
/// </summary>
/// <param name="Reason">Why it is waiting or ended, in the §18.6 wording.</param>
/// <param name="ActualBitsPerSecond">Bytes written over recorded time, so far.</param>
/// <param name="SourceId">The source captured now (it changes with a switch).</param>
/// <param name="StartOffsetMs">The track's first frame on the recording timeline (0 before it).</param>
public sealed record VideoSessionStatus(
    string TrackId,
    VideoSessionState State,
    string SourceName,
    string? Reason,
    VideoCaptureException? Error,
    string EncoderName,
    bool HardwareEncoder,
    int Width,
    int Height,
    int FrameRate,
    int TargetBitsPerSecond,
    long ActualBitsPerSecond,
    long FramesWritten,
    long FramesDropped,
    long BytesWritten,
    long DurationMs,
    string SourceId = "",
    long StartOffsetMs = 0);
