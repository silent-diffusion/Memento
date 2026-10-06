namespace Memento.Core.Bridge.Contracts;

/// <summary>When the SHA-256 hashes of the tracks and mix were computed; <c>null</c> before finalize.</summary>
public sealed record IntegrityInfo(string Algorithm, DateTimeOffset? ComputedAt);
