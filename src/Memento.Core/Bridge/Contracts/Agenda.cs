namespace Memento.Core.Bridge.Contracts;

/// <summary>The recording's agenda. Import arrives in M3; until then it is edited by hand or empty.</summary>
/// <param name="Source">The imported file name, e.g. <c>agenda.docx</c>.</param>
public sealed record Agenda(string? Source, bool ParsedLocally, IReadOnlyList<AgendaItem> Items)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    public IReadOnlyList<AgendaItem> Items { get; set; } = Items ?? [];

    public static Agenda Empty { get; } = new(null, false, []);
}
