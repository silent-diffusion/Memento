namespace Memento.Core.Bridge.Contracts;

/// <summary>A stretch of 10 s or more with speech energy on a track but no transcript (BRIDGE.md, Shared types (M2)).</summary>
/// <param name="Start">Seconds on the recording timeline.</param>
/// <param name="End">Seconds on the recording timeline.</param>
/// <param name="Track">The track it was found on.</param>
public sealed record CoverageGap(double Start, double End, string? Track);
