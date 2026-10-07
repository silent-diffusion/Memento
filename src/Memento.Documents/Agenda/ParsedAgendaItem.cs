namespace Memento.Documents.Agenda;

/// <summary>
/// One agenda item as parsed. Maps onto the bridge's <c>AgendaItem</c> (<c>text</c>, <c>uncertain</c>,
/// <c>uncertainReason</c>); the extra fields let the UI indent sub-items and point back into the source.
/// </summary>
/// <param name="Text">The item text with its number, bullet and time prefix removed.</param>
/// <param name="Uncertain">The parser is not sure this item is right; <paramref name="UncertainReason"/> says why.</param>
/// <param name="UncertainReason">A user-facing sentence (DESIGN.md §17): what may be wrong and how to fix it.</param>
/// <param name="Level">0 for a top-level item, 1 for its sub-items, and so on. Levels never skip.</param>
/// <param name="Location">Where the item is in the source.</param>
/// <param name="Time">A time prefix as written ("10:00", "9:30–10:15", "2 pm"), or <c>null</c>.</param>
/// <param name="Number">The number or letter as written ("3", "2.1", "b"), or <c>null</c> for bullets and plain lines.</param>
public sealed record ParsedAgendaItem(
    string Text,
    bool Uncertain,
    string? UncertainReason,
    int Level,
    AgendaSourceLocation Location,
    string? Time = null,
    string? Number = null);
