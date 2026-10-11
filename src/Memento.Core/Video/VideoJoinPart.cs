namespace Memento.Core.Video;

/// <summary>One file of a track to join, with where it starts relative to the track's first part.</summary>
public sealed record VideoJoinPart(string Path, long StartMs);
