namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>audio.remove</c> (2.1).</summary>
/// <param name="FreedBytes">The size of the audio files deleted.</param>
public sealed record AudioRemoveResult(long FreedBytes);
