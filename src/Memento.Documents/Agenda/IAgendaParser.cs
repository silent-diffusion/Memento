namespace Memento.Documents.Agenda;

/// <summary>
/// Turns one kind of agenda source into an ordered list of items. Parsers run on this PC only and never touch the
/// network. Use <see cref="AgendaImporter"/> to pick the right parser for a file: it sniffs the content, so a file is
/// never parsed by its extension alone.
/// </summary>
public interface IAgendaParser
{
    /// <summary>The kinds of source this parser reads.</summary>
    IReadOnlyList<AgendaSourceKind> Kinds { get; }

    /// <summary>Whether the parser reads files with this name (by extension) or this MIME content type.</summary>
    bool CanParse(string fileName, string? contentType);

    /// <summary>
    /// Parses the agenda. Throws <see cref="AgendaImportException"/> with an <see cref="AgendaErrorCodes"/> code when the
    /// content is too large, unreadable or holds no agenda items, and <see cref="OperationCanceledException"/> when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken);
}
