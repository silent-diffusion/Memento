namespace Memento.Audio.Recording;

/// <summary>Live view of one track (BRIDGE.md <c>Track</c> while recording).</summary>
/// <param name="SourceId">The bridge source id.</param>
/// <param name="Kind">Source kind.</param>
/// <param name="Name">Display name of the source.</param>
/// <param name="FileStem">File stem in <c>tracks/</c> (<c>mic</c> → <c>mic.wav</c>).</param>
/// <param name="Parts">WAV parts written so far.</param>
/// <param name="SampleRate">Stored sample rate.</param>
/// <param name="Channels">Stored channels.</param>
/// <param name="Duration">Audio written so far.</param>
/// <param name="StartOffset">Timeline position of the track's first frame (0 unless added mid-session).</param>
/// <param name="EndedEarlyAt">Timeline time the track ended before the session, or null.</param>
/// <param name="EndReason">Why it ended, or null while active.</param>
public sealed record TrackStatus(
    string SourceId,
    AudioSourceKind Kind,
    string Name,
    string FileStem,
    IReadOnlyList<string> Parts,
    int SampleRate,
    int Channels,
    TimeSpan Duration,
    TimeSpan StartOffset,
    TimeSpan? EndedEarlyAt,
    TrackEndReason? EndReason)
{
    public bool IsActive => EndReason is null;
}
