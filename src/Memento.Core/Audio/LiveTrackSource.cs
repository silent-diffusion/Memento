namespace Memento.Core.Audio;

/// <summary>One track of a recording in progress, as the live transcript reads it (2.0).</summary>
/// <param name="File">Relative capture file (<c>tracks/mic.wav</c>); later RIFF parts are found beside it.</param>
/// <param name="StartOffsetMs">Where the track starts on the recording timeline.</param>
/// <param name="EndedAtMs">Where it ended, when it ended before the session.</param>
public sealed record LiveTrackSource(string TrackId, string ProjectFolder, string File, long StartOffsetMs, long? EndedAtMs);
