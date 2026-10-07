namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters naming one attachment of one recording.</summary>
public sealed record AttachmentIdParams
{
    public required string RecordingId { get; init; }

    public required string AttachmentId { get; init; }
}
