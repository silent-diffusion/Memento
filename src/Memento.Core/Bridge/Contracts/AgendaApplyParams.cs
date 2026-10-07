namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>agenda.apply</c>: the reviewed items replace the recording's agenda.</summary>
public sealed record AgendaApplyParams
{
    public required string RecordingId { get; init; }

    public IReadOnlyList<AgendaApplyItem> Items { get; init; } = [];

    /// <summary>The preview's <c>source</c>: the file name, or "Pasted text".</summary>
    public required string Source { get; init; }

    public required string SourceKind { get; init; }

    /// <summary>The preview's token; the original file is copied into <c>attachments/</c> when it is given.</summary>
    public string? AttachmentToken { get; init; }
}
