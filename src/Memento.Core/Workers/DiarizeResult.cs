namespace Memento.Core.Workers;

/// <summary>The end of a speaker job.</summary>
public sealed record DiarizeResult(IReadOnlyList<DiarizedTrack> Tracks, double AudioSeconds, long ElapsedMs);
