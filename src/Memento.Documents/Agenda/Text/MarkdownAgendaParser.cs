using System.Text.RegularExpressions;
using Memento.Documents.Agenda.Tables;

namespace Memento.Documents.Agenda.Text;

/// <summary>Markdown: headings (ATX and setext), nested lists, task checkboxes, tables, inline markup.</summary>
public sealed partial class MarkdownAgendaParser : IAgendaParser
{
    public IReadOnlyList<AgendaSourceKind> Kinds { get; } = [AgendaSourceKind.Markdown];

    public bool CanParse(string fileName, string? contentType) =>
        AgendaResults.HasExtension(fileName, ".md", ".markdown", ".mdown", ".mkd") || AgendaResults.HasContentType(contentType, "text/markdown", "text/x-markdown");

    public async Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await AgendaContent.ReadAsync(content, options, cancellationToken).ConfigureAwait(false);
        return await ParseGuard.RunAsync(
            options,
            "a Markdown file",
            token =>
            {
                var text = TextDecoder.Decode(bytes.Span, out var fallback);
                return Parse(text, options, fallback ? [TextDecoder.FallbackWarning(options)] : [], token);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether pasted text is Markdown: it has headings, task boxes or a table.</summary>
    internal static bool LooksLikeMarkdown(string text) =>
        AtxHeadingPattern().IsMatch(text) || TaskPattern().IsMatch(text) || TableSeparatorPattern().IsMatch(text);

    internal static AgendaParseResult Parse(string text, AgendaParseOptions options, IReadOnlyList<AgendaParseWarning> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = TextLines.Split(text);
        var lines = new List<SourceLine>(raw.Count);
        var formatWarnings = new List<AgendaParseWarning>(warnings);
        var i = 0;

        if (raw.Count > 2 && raw[0].Trim() == "---")
        {
            var end = raw.ToList().FindIndex(1, l => l.Trim() is "---" or "...");
            if (end > 0 && end < 60)
            {
                var front = raw.Skip(1).Take(end - 1).Where(l => l.Trim().Length > 0).ToList();
                if (front.Count > 0)
                {
                    formatWarnings.Add(new AgendaParseWarning(
                        AgendaWarningCodes.DetailsSkipped,
                        "The document's front matter (the block between --- lines at the top) was left out of the list. Add anything from it that belongs in the agenda here.",
                        string.Join('\n', front),
                        AgendaSourceLocation.AtLine(2)));
                }

                i = end + 1;
            }
        }

        var inFence = false;
        for (; i < raw.Count; i++)
        {
            if ((i & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var location = AgendaSourceLocation.AtLine(i + 1);
            var line = raw[i];
            var trimmed = line.Trim();

            if (FencePattern().IsMatch(trimmed))
            {
                inFence = !inFence;
                lines.Add(SourceLine.Blank(location));
                continue;
            }

            if (inFence)
            {
                var (fenceIndent, fenceText) = TextLines.Measure(line);
                lines.Add(fenceText.Length == 0 ? SourceLine.Blank(location) : new SourceLine(fenceText, location) { Indent = fenceIndent });
                continue;
            }

            if (trimmed.Length == 0 || RulePattern().IsMatch(trimmed) || LinkDefinitionPattern().IsMatch(trimmed))
            {
                lines.Add(SourceLine.Blank(location));
                continue;
            }

            if (IsTableStart(raw, i))
            {
                var rows = new List<TableRow>();
                for (; i < raw.Count && raw[i].Trim().Contains('|', StringComparison.Ordinal); i++)
                {
                    if (TableSeparatorPattern().IsMatch(raw[i]))
                    {
                        continue;
                    }

                    rows.Add(new TableRow(SplitTableRow(raw[i]).Select(MarkdownInline.Strip).ToList(), AgendaSourceLocation.AtLine(i + 1)));
                }

                i--;
                var table = AgendaTableReader.Read(rows, cancellationToken);
                lines.AddRange(table.Lines);
                formatWarnings.AddRange(table.Warnings);
                continue;
            }

            var atx = AtxHeadingPattern().Match(line);
            if (atx.Success)
            {
                lines.Add(new SourceLine(MarkdownInline.Strip(atx.Groups["text"].Value.Trim()), location) { HeadingLevel = atx.Groups["hashes"].Length });
                continue;
            }

            if (i + 1 < raw.Count && !MarkerParser.TryParseMarker(trimmed, out _, out _))
            {
                var underline = raw[i + 1].Trim();
                if (SetextPattern().Match(underline) is { Success: true } setext && TextLines.Measure(line).Indent < 4)
                {
                    lines.Add(new SourceLine(MarkdownInline.Strip(trimmed), location) { HeadingLevel = setext.Groups["c"].Value == "=" ? 1 : 2 });
                    lines.Add(SourceLine.Blank(AgendaSourceLocation.AtLine(i + 2)));
                    i++;
                    continue;
                }
            }

            var (indent, content) = TextLines.Measure(line);
            while (content.StartsWith('>'))
            {
                content = content[1..].TrimStart();
            }

            var stripped = MarkdownInline.Strip(content).Trim();
            if (content.EndsWith("  ", StringComparison.Ordinal) || content.EndsWith('\\'))
            {
                stripped = stripped.TrimEnd('\\').TrimEnd();
            }

            lines.Add(stripped.Length == 0 ? SourceLine.Blank(location) : new SourceLine(stripped, location) { Indent = indent });
        }

        var structured = AgendaStructurer.Structure(lines, cancellationToken);
        var kind = options.SourceKind == AgendaSourceKind.PastedText ? AgendaSourceKind.PastedText : AgendaSourceKind.Markdown;
        return AgendaResults.Create(kind, options, structured, formatWarnings);
    }

    private static bool IsTableStart(IReadOnlyList<string> raw, int index) =>
        raw[index].Contains('|', StringComparison.Ordinal) && index + 1 < raw.Count && TableSeparatorPattern().IsMatch(raw[index + 1]);

    private static List<string> SplitTableRow(string line)
    {
        var text = line.Trim();
        if (text.StartsWith('|'))
        {
            text = text[1..];
        }

        if (text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal))
        {
            text = text[..^1];
        }

        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (text[i] == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(text[i]);
            }
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})\s+(?<text>.*?)(?:\s+#+)?\s*$", RegexOptions.Multiline)]
    private static partial Regex AtxHeadingPattern();

    [GeneratedRegex(@"^(?<c>=|-)\k<c>{2,}\s*$")]
    private static partial Regex SetextPattern();

    [GeneratedRegex(@"^\s*[-*+]\s+\[[ xX]\]\s", RegexOptions.Multiline)]
    private static partial Regex TaskPattern();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.Multiline)]
    private static partial Regex TableSeparatorPattern();

    [GeneratedRegex(@"^(?:`{3,}|~{3,})")]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"^(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$")]
    private static partial Regex RulePattern();

    [GeneratedRegex(@"^\[[^\]]+\]:\s+\S+")]
    private static partial Regex LinkDefinitionPattern();
}
