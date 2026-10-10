namespace Memento.Video.Recovery;

/// <summary>What recovery kept.</summary>
/// <param name="Readable">The capture file had its header and is a fragmented MP4.</param>
/// <param name="Fragments">Complete fragments kept.</param>
/// <param name="DurationSeconds">Video kept, from the first to the end of the last complete fragment.</param>
/// <param name="TornBytesRemoved">Bytes of an incomplete last fragment cut off.</param>
/// <param name="OutputBytes">Size of the remuxed file (0 when only repaired).</param>
public sealed record VideoRecoveryResult(bool Readable, int Fragments, double DurationSeconds, long TornBytesRemoved, long Samples, long OutputBytes);
