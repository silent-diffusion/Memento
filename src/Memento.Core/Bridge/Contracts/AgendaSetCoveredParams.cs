namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>agenda.setCovered</c>.</summary>
public sealed record AgendaSetCoveredParams
{
    public required string RecordingId { get; init; }

    public required string ItemId { get; init; }

    public required bool Covered { get; init; }
}
