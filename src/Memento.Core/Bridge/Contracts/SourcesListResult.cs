namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>sources.list</c>. Video capture is not in 1.0, so <paramref name="VideoAvailable"/> is false.</summary>
public sealed record SourcesListResult(IReadOnlyList<AudioSource> Audio, bool VideoAvailable);
