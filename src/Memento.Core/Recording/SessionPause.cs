namespace Memento.Core.Recording;

/// <summary>A pause: writing stopped at <paramref name="AtMs"/> of recorded time; <paramref name="DurationMs"/> is null while still paused.</summary>
public sealed record SessionPause(long AtMs, DateTimeOffset PausedAt, long? DurationMs);
