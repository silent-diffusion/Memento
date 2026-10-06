namespace Memento.Core.Bridge.Contracts;

/// <summary>The recording's agenda. Import arrives in M3; until then it is edited by hand or empty.</summary>
/// <param name="Source">The imported file name, e.g. <c>agenda.docx</c>.</param>
public sealed record Agenda(string? Source, bool ParsedLocally, IReadOnlyList<AgendaItem> Items)
{
    public static Agenda Empty { get; } = new(null, false, []);
}
