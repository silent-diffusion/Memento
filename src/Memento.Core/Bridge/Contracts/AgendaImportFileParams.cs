namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>agenda.importFile</c>. The host shows its file picker; <see cref="Path"/> is refused with
/// <c>bridge.invalidParams</c> (<see cref="PickedFilesOnly"/>) and is kept only so the request shape stays stable.
/// </summary>
public sealed record AgendaImportFileParams
{
    /// <summary><c>null</c> before the recording exists (M3 clarification 2); the token is then accepted by any <c>agenda.apply</c>.</summary>
    public string? RecordingId { get; init; }

    public string? Path { get; init; }
}
