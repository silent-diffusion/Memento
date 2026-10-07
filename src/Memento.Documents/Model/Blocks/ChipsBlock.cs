namespace Memento.Documents.Model.Blocks;

/// <summary>Short labels shown as pills (Participants).</summary>
public sealed record ChipsBlock : Block
{
    public override string Type => BlockTypes.Chips;

    public IReadOnlyList<string> Items { get; init; } = [];
}
