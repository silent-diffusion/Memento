using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.Tables;

/// <summary>The lines a table contributes, and what the reader noticed.</summary>
/// <param name="Lines">The agenda column as lines, with times and section rows.</param>
/// <param name="Warnings">Unused columns, rows without an item.</param>
/// <param name="HasAgendaHeader">The table has a header naming an agenda-like column ("Topic", "Agenda item").</param>
internal sealed record TableReadResult(IReadOnlyList<SourceLine> Lines, IReadOnlyList<AgendaParseWarning> Warnings, bool HasAgendaHeader);
