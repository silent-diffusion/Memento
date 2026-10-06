namespace Memento.Core.Bridge.Contracts;

/// <summary>The session's tracks after a source change.</summary>
public sealed record TracksResult(IReadOnlyList<Track> Tracks);
