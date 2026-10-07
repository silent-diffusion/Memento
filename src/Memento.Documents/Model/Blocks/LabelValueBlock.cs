namespace Memento.Documents.Model.Blocks;

/// <summary>Label/value pairs (Meeting purpose, Next meeting).</summary>
public sealed record LabelValueBlock : Block
{
    public override string Type => BlockTypes.LabelValue;

    public IReadOnlyList<LabelValuePair> Pairs { get; init; } = [];
}
