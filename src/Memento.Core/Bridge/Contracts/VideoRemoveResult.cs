namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>video.remove</c>.</summary>
/// <param name="FreedBytes">The size of the video files removed.</param>
public sealed record VideoRemoveResult(long FreedBytes);
