namespace Memento.Core.Bridge.Contracts;

/// <summary>A highlight. <paramref name="SegmentId"/> links a transcript segment from M2.</summary>
public sealed record Highlight(string Id, long AtMs, string Note, string Origin, string? SegmentId);
