namespace Memento.Core.Recording;

/// <summary>The stored mix.</summary>
public sealed record FinalizedFile(string File, string Codec, int SampleRate, int Channels, long DurationMs, long SizeBytes, string Sha256);
