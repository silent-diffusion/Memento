namespace Memento.Documents.Model.Blocks;

/// <summary>An indented quotation with an optional attribution ("Lee Chen") and the moment it was said.</summary>
public sealed record QuoteBlock : Block
{
    public override string Type => BlockTypes.Quote;

    public IReadOnlyList<Run> Runs { get; init; } = [];

    public string? Attribution { get; init; }

    /// <summary>When it was said, in seconds; shown after the attribution and linked to the transcript.</summary>
    public double? T { get; init; }
}
