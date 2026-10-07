namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>agenda.importDropped</c>: the names of the files dropped on the agenda drop zone. The host matches
/// them against the paths WebView2 handed it with the same message; the first one it can resolve is imported.
/// </summary>
public sealed record AgendaImportDroppedParams
{
    public required string RecordingId { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];
}
