namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>sources.list</c>. <paramref name="VideoAvailable"/> (2.0): display, window or camera capture works on this PC (<c>video.listSources</c> lists them).</summary>
public sealed record SourcesListResult(IReadOnlyList<AudioSource> Audio, bool VideoAvailable);
