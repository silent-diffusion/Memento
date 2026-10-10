namespace Memento.Core.Video;

/// <summary>What a join wrote.</summary>
/// <param name="Reencoded">The parts came from different encoders, so they were encoded again.</param>
public sealed record VideoJoinResult(long Frames, long DurationMs, long Bytes, bool Reencoded);
