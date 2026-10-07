namespace Memento.Core.Bridge.Contracts;

/// <summary>A stretch longer than 20 s with speech energy on a track but no transcript (proposed for BRIDGE.md).</summary>
/// <param name="Start">Seconds on the recording timeline.</param>
/// <param name="End">Seconds on the recording timeline.</param>
/// <param name="Track">The track it was found on.</param>
public sealed record CoverageGap(double Start, double End, string? Track);
