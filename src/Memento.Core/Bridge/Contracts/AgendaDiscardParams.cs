namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>agenda.discard</c>.</summary>
public sealed record AgendaDiscardParams
{
    public required string AttachmentToken { get; init; }
}
