namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>agenda.importFile</c>. Without <see cref="Path"/> the host shows the file picker.</summary>
public sealed record AgendaImportFileParams
{
    public required string RecordingId { get; init; }

    public string? Path { get; init; }
}
