namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>annotations.addHighlight</c> and <c>annotations.updateHighlight</c>.</summary>
public sealed record HighlightParams
{
    public required string RecordingId { get; init; }

    public required HighlightPatch Highlight { get; init; }
}
