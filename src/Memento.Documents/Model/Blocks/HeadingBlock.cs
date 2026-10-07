namespace Memento.Documents.Model.Blocks;

/// <summary>A sub-heading inside a module. <see cref="Level"/> 1 is the first level below the module's own heading (1–3).</summary>
public sealed record HeadingBlock : Block
{
    public override string Type => BlockTypes.Heading;

    public int Level { get; init; } = 1;

    public IReadOnlyList<Run> Runs { get; init; } = [];
}
