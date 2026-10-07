using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.Tables;

/// <summary>
/// CSV and TSV files (and cells copied from a spreadsheet): header detection, the agenda-like column, quoted fields.
/// </summary>
public sealed class DelimitedAgendaParser : IAgendaParser
{
    public IReadOnlyList<AgendaSourceKind> Kinds { get; } = [AgendaSourceKind.Csv, AgendaSourceKind.Tsv];

    public bool CanParse(string fileName, string? contentType) =>
        AgendaResults.HasExtension(fileName, ".csv", ".tsv", ".tab") ||
        AgendaResults.HasContentType(contentType, "text/csv", "text/tab-separated-values", "application/csv", "text/comma-separated-values");

    public async Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await AgendaContent.ReadAsync(content, options, cancellationToken).ConfigureAwait(false);
        var text = TextDecoder.Decode(bytes.Span, out var fallback);
        return Parse(text, options, fallback ? [TextDecoder.FallbackWarning(options)] : [], cancellationToken);
    }

    internal static AgendaParseResult Parse(string text, AgendaParseOptions options, IReadOnlyList<AgendaParseWarning> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tsv = options.SourceKind == AgendaSourceKind.Tsv ||
            (options.SourceKind is null && (AgendaResults.HasExtension(options.FileName ?? string.Empty, ".tsv", ".tab") ||
                                            AgendaResults.HasContentType(options.ContentType, "text/tab-separated-values")));
        var delimiter = DelimitedReader.DetectDelimiter(text, tsv);
        var rows = DelimitedReader.Read(text, delimiter, cancellationToken)
            .Select((cells, index) => new TableRow(cells, new AgendaSourceLocation { Row = index + 1 }))
            .ToList();
        var table = AgendaTableReader.Read(rows, cancellationToken);
        var structured = AgendaStructurer.Structure(table.Lines, cancellationToken);
        var kind = delimiter == '\t' ? AgendaSourceKind.Tsv : AgendaSourceKind.Csv;
        return AgendaResults.Create(kind, options, structured, [.. warnings, .. table.Warnings]);
    }
}
