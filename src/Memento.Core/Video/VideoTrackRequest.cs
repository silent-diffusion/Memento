using Memento.Core.Settings;

namespace Memento.Core.Video;

/// <summary>What one video track records and how.</summary>
/// <param name="TrackId"><c>screen</c>, <c>camera</c>, <c>screen-2</c>: unique in the project; the file is <c>tracks/&lt;id&gt;.mp4</c>.</param>
/// <param name="Role"><c>screen</c> or <c>camera</c>.</param>
/// <param name="SourceId">A <c>VideoSource</c> id of the role's kind.</param>
/// <param name="TimelineOriginQpc">
/// Recording position 0 on the QPC clock in 100 ns units (<see cref="VideoClock"/>), with the pauses so far left out: the
/// audio session's start plus the paused time before this track starts, so its frames land where the audio is.
/// </param>
/// <param name="Settings">Settings › Recording › Video defaults as they were when the recording started.</param>
public sealed record VideoTrackRequest(
    string TrackId,
    string Role,
    string SourceId,
    string ProjectFolder,
    long TimelineOriginQpc,
    VideoSettings Settings);
