namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>video.listSources</c>: displays (primary first), top-level windows and cameras.</summary>
public sealed record VideoSourcesResult(IReadOnlyList<VideoSource> Displays, IReadOnlyList<VideoSource> Windows, IReadOnlyList<VideoSource> Cameras);
