namespace Memento.Core.Bridge.Contracts;

/// <summary>One reviewed agenda item sent with <c>agenda.apply</c>.</summary>
public sealed record AgendaApplyItem
{
    public required string Text { get; init; }

    public bool Uncertain { get; init; }

    public string? UncertainReason { get; init; }
}
