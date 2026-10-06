namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>recording.markHighlight</c>.</summary>
public sealed record RecordingMarkHighlightParams
{
    public required string SessionId { get; init; }

    public string? Note { get; init; }
}
