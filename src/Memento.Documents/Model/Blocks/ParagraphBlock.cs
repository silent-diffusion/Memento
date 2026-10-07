namespace Memento.Documents.Model.Blocks;

/// <summary>A paragraph of runs. A line break inside it is a <c>\n</c> in a run's text.</summary>
public sealed record ParagraphBlock : Block
{
    public override string Type => BlockTypes.Paragraph;

    public IReadOnlyList<Run> Runs { get; init; } = [];
}
