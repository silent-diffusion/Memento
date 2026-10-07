namespace Memento.Documents.Model.Blocks;

/// <summary>Moments in time order (Timeline, Chapter).</summary>
public sealed record TimelineBlock : Block
{
    public override string Type => BlockTypes.Timeline;

    public IReadOnlyList<TimelineEntry> Entries { get; init; } = [];
}
