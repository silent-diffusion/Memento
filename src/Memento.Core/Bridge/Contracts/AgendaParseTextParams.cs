namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>agenda.parseText</c>: pasted text.</summary>
public sealed record AgendaParseTextParams
{
    /// <summary><c>null</c> before the recording exists (M3 clarification 2); the token is then accepted by any <c>agenda.apply</c>.</summary>
    public string? RecordingId { get; init; }

    public required string Text { get; init; }
}
