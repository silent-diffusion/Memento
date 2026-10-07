namespace Memento.Documents.Model.Blocks;

/// <summary>A bulleted or numbered list; items can nest (<see cref="ListItem.Items"/>). Nested lists use the same style.</summary>
public sealed record ListBlock : Block
{
    public override string Type => BlockTypes.List;

    public ListStyle Style { get; init; } = ListStyle.Bulleted;

    public IReadOnlyList<ListItem> Items { get; init; } = [];
}
