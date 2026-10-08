namespace Memento.Documents.Agenda.Text;

/// <summary>
/// Plain text files and pasted text: numbered, lettered and bulleted lists, indented sub-items, "Agenda:" headings,
/// time prefixes ("10:00 – Welcome") and any line ending.
/// </summary>
public sealed class PlainTextAgendaParser : IAgendaParser
{
    public IReadOnlyList<AgendaSourceKind> Kinds { get; } = [AgendaSourceKind.Text, AgendaSourceKind.PastedText];

    public bool CanParse(string fileName, string? contentType) =>
        AgendaResults.HasExtension(fileName, ".txt", ".text", ".log") || AgendaResults.HasContentType(contentType, "text/plain");

    public async Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await AgendaContent.ReadAsync(content, options, cancellationToken).ConfigureAwait(false);
        return await ParseGuard.RunAsync(
            options,
            "a text file",
            token =>
            {
                var text = TextDecoder.Decode(bytes.Span, out var fallback);
                return Parse(text, options, fallback ? [TextDecoder.FallbackWarning(options)] : [], token);
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal static AgendaParseResult Parse(string text, AgendaParseOptions options, IReadOnlyList<AgendaParseWarning> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lines = TextLines.ToSourceLines(text);
        var structured = AgendaStructurer.Structure(lines, cancellationToken);
        var kind = options.SourceKind == AgendaSourceKind.PastedText ? AgendaSourceKind.PastedText : AgendaSourceKind.Text;
        return AgendaResults.Create(kind, options, structured, warnings);
    }
}
