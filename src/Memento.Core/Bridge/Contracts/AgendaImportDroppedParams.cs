namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>agenda.importDropped</c>: the names of the files dropped on the agenda drop zone. The host matches
/// them against the paths WebView2 handed it with the same message; the first one it can resolve is imported.
/// </summary>
public sealed record AgendaImportDroppedParams
{
    /// <summary><c>null</c> before the recording exists (M3 clarification 2); the token is then accepted by any <c>agenda.apply</c>.</summary>
    public string? RecordingId { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];
}
