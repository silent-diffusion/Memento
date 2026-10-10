namespace Memento.Core.Video;

/// <summary>What <see cref="IVideoFiles.Repair"/> found.</summary>
/// <param name="Readable">The file has its header and complete frames.</param>
/// <param name="Frames">Frames kept.</param>
/// <param name="DurationMs">Video kept.</param>
/// <param name="TornBytesRemoved">Bytes of an incomplete last fragment cut off.</param>
public sealed record VideoFileRepair(bool Readable, long Frames, long DurationMs, long TornBytesRemoved);
