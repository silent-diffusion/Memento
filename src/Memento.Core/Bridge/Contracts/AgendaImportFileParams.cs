namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>agenda.importFile</c>. Without <see cref="Path"/> the host shows the file picker.</summary>
public sealed record AgendaImportFileParams
{
    /// <summary><c>null</c> before the recording exists (M3 clarification 2); the token is then accepted by any <c>agenda.apply</c>.</summary>
    public string? RecordingId { get; init; }

    public string? Path { get; init; }
}
