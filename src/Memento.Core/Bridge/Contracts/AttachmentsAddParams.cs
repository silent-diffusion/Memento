namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>attachments.add</c>. The host shows its file picker; <see cref="Path"/> is refused with
/// <c>bridge.invalidParams</c> (<see cref="PickedFilesOnly"/>) and is kept only so the request shape stays stable.
/// </summary>
public sealed record AttachmentsAddParams
{
    public required string RecordingId { get; init; }

    public string? Path { get; init; }
}
