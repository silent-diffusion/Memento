namespace Memento.Core.Recording;

/// <summary>One stored track.</summary>
public sealed record FinalizedTrack(string TrackId, string File, string Codec, int SampleRate, int Channels, long DurationMs, long SizeBytes, string Sha256);
