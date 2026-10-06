using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Recording;

/// <summary>One track of a session.</summary>
/// <param name="TrackId">Unique in the project: <c>mic</c>, <c>system</c>, <c>app-zoom</c>, <c>mic-2</c> (see <see cref="TrackNaming"/>).</param>
/// <param name="File">Relative to the project folder, forward slashes: <c>tracks/mic.wav</c>.</param>
/// <param name="StartOffsetMs">Where the track starts on the session timeline.</param>
/// <param name="DataBytes">Sample bytes written so far.</param>
/// <param name="CheckpointedBytes">Sample bytes covered by the header on disk at the last checkpoint.</param>
/// <param name="EndedAtMs">Session time at which the track ended before the session did.</param>
public sealed record SessionTrack(
    string TrackId,
    AudioSource Source,
    string File,
    PcmFormat Format,
    long StartOffsetMs,
    long DataBytes,
    long CheckpointedBytes,
    long? EndedAtMs,
    TrackEndReason? EndReason)
{
    public long DurationMs => Format.BytesToMilliseconds(DataBytes);

    public bool IsOpen => EndedAtMs is null && EndReason is null;
}
