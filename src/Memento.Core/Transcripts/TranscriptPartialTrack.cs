namespace Memento.Core.Transcripts;

/// <summary>A track's progress in an unfinished pass.</summary>
/// <param name="WindowsDone">Windows whose segments are final (the next pass starts at this window).</param>
/// <param name="Speech">Speech-energy regions, <c>[start, end]</c> seconds, for the coverage check.</param>
public sealed record TranscriptPartialTrack(string TrackId, int WindowsDone, int Windows, bool Silent, double DurationSeconds, IReadOnlyList<double[]> Speech);
