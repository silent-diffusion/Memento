namespace Memento.Core.Bridge.Contracts;

/// <summary><c>Partial&lt;Highlight&gt;</c>. Add ignores <see cref="Id"/> (the host assigns it); update requires it.</summary>
public sealed record HighlightPatch
{
    public string? Id { get; init; }

    public long? AtMs { get; init; }

    public string? Note { get; init; }

    public string? Origin { get; init; }

    public string? SegmentId { get; init; }
}
