namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>attachments.add</c>. Without <see cref="Path"/> the host shows the file picker.</summary>
public sealed record AttachmentsAddParams
{
    public required string RecordingId { get; init; }

    public string? Path { get; init; }
}
