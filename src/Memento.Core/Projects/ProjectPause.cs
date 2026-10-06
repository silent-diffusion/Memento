namespace Memento.Core.Projects;

/// <summary>A pause: recording stopped at <paramref name="AtMs"/> of recorded time for <paramref name="DurationMs"/> of wall time.</summary>
public sealed record ProjectPause(long AtMs, DateTimeOffset PausedAt, long DurationMs);
