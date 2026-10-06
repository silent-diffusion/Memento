using Memento.Audio.Capture;
using Memento.Audio.Writing;

namespace Memento.Audio.Recording;

/// <summary>A finished track: what Core puts in the manifest and hands to finalize (FLAC, mix, peaks).</summary>
/// <param name="SourceId">The bridge source id.</param>
/// <param name="Kind">Source kind.</param>
/// <param name="Name">Display name of the source.</param>
/// <param name="FileStem">File stem in <c>tracks/</c>.</param>
/// <param name="Parts">WAV parts in order (open with <see cref="WavTrackSet.FromParts"/>).</param>
/// <param name="CaptureFormat">What the device delivered (float32 48 kHz stereo on current Windows).</param>
/// <param name="StorageFormat">What is on disk (int24 or int16).</param>
/// <param name="Frames">Frames written.</param>
/// <param name="Duration">Frames / rate.</param>
/// <param name="StartOffset">Timeline position of the first frame.</param>
/// <param name="EndedEarlyAt">Timeline time the track ended before the session (lost, disabled, write failure), or null.</param>
/// <param name="EndReason">Why it ended.</param>
/// <param name="Gaps">Pauses inside this track (position in the track, duration on the clock).</param>
/// <param name="DriftPpm">Device clock vs QPC over the whole track, or null if too short.</param>
/// <param name="Statistics">Capture counters (overruns, synthesized silence, discontinuities).</param>
/// <param name="Error">The specific failure message for <see cref="TrackEndReason.SourceLost"/> or <see cref="TrackEndReason.WriteFailed"/>.</param>
public sealed record TrackResult(
    string SourceId,
    AudioSourceKind Kind,
    string Name,
    string FileStem,
    IReadOnlyList<string> Parts,
    AudioFormat CaptureFormat,
    AudioFormat StorageFormat,
    long Frames,
    TimeSpan Duration,
    TimeSpan StartOffset,
    TimeSpan? EndedEarlyAt,
    TrackEndReason EndReason,
    IReadOnlyList<TrackGap> Gaps,
    double? DriftPpm,
    CaptureStatistics Statistics,
    string? Error);
