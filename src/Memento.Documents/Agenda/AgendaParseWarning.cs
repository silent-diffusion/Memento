namespace Memento.Documents.Agenda;

/// <summary>
/// Something the user should know about the import that is not tied to one item. Content that was read but not
/// turned into items is never dropped silently: it is carried in <paramref name="Content"/>.
/// </summary>
/// <param name="Code">One of <see cref="AgendaWarningCodes"/>.</param>
/// <param name="Message">A user-facing sentence (DESIGN.md §17).</param>
/// <param name="Content">The text that was left out, when there is any, one entry per line.</param>
/// <param name="Location">Where in the source, when the warning is about one place.</param>
public sealed record AgendaParseWarning(
    string Code,
    string Message,
    string? Content = null,
    AgendaSourceLocation? Location = null);
