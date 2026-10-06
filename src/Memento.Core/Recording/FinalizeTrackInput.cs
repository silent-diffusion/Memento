namespace Memento.Core.Recording;

/// <summary>One capture file to finalize.</summary>
/// <param name="CaptureFile">Relative WAV path, e.g. <c>tracks/mic.wav</c>.</param>
/// <param name="StartOffsetMs">Where the track starts on the session timeline, for the mix.</param>
public sealed record FinalizeTrackInput(string TrackId, string CaptureFile, long StartOffsetMs);
