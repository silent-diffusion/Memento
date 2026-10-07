namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>agenda.parseText</c>: pasted text.</summary>
public sealed record AgendaParseTextParams
{
    public required string RecordingId { get; init; }

    public required string Text { get; init; }
}
