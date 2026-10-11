namespace Memento.Core.Recording;

/// <summary>One stored track for <see cref="MixRebuildRequest"/>.</summary>
/// <param name="File">Relative path of the stored file (<c>tracks/mic.flac</c>).</param>
/// <param name="StartOffsetMs">Where the track starts on the recording timeline.</param>
/// <param name="EndedAtMs">Timeline time the track ended before the recording did, or <c>null</c>.</param>
public sealed record MixRebuildTrack(string TrackId, string File, long StartOffsetMs, long? EndedAtMs);
