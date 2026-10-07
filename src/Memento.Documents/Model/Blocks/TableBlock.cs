namespace Memento.Documents.Model.Blocks;

/// <summary>A table with named columns (Action items: Action, Owner, Due).</summary>
public sealed record TableBlock : Block
{
    public override string Type => BlockTypes.Table;

    /// <summary>The header row.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Relative column widths (2, 1, 1); empty means equal widths.</summary>
    public IReadOnlyList<double> Widths { get; init; } = [];

    public IReadOnlyList<TableRow> Rows { get; init; } = [];
}
