namespace Memento.Core.Recording;

/// <summary>One capture track to finalize.</summary>
/// <param name="CaptureFile">
/// Relative WAV path of the first part, e.g. <c>tracks/mic.wav</c>. A track longer than one RIFF file continues in
/// <c>tracks/mic.part2.wav</c>, <c>tracks/mic.part3.wav</c>, … (see <see cref="CaptureParts"/>).
/// </param>
/// <param name="StartOffsetMs">Where the track starts on the session timeline, for the mix.</param>
/// <param name="EndedAtMs">Timeline time the track ended before the session, or <c>null</c>.</param>
public sealed record FinalizeTrackInput(string TrackId, string CaptureFile, long StartOffsetMs, long? EndedAtMs = null);
