namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>annotations.removeHighlight</c>.</summary>
public sealed record HighlightIdParams
{
    public required string RecordingId { get; init; }

    public required string HighlightId { get; init; }
}
