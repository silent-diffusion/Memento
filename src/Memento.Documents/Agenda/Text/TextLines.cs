namespace Memento.Documents.Agenda.Text;

/// <summary>Splits text into lines (Windows, Mac and Unix line endings) and measures their indentation.</summary>
internal static class TextLines
{
    public const int TabWidth = 4;

    public static IReadOnlyList<string> Split(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    /// <summary>The indentation of a line in space-widths (a tab counts as <see cref="TabWidth"/>), and the line without it.</summary>
    public static (int Indent, string Text) Measure(string line)
    {
        var indent = 0;
        var i = 0;
        for (; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\t')
            {
                indent += TabWidth - (indent % TabWidth);
            }
            else if (c is ' ' or ' ' or '　')
            {
                indent++;
            }
            else
            {
                break;
            }
        }

        return (indent, line[i..].TrimEnd());
    }

    /// <summary>Plain text lines with their indentation and 1-based line numbers. Email quote marks ("> ") are removed.</summary>
    public static List<SourceLine> ToSourceLines(string text, int firstLineNumber = 1)
    {
        var lines = Split(text);
        var result = new List<SourceLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            var location = AgendaSourceLocation.AtLine(firstLineNumber + i);
            var unquoted = StripQuote(raw);
            var (indent, content) = Measure(unquoted);
            result.Add(content.Length == 0 ? SourceLine.Blank(location) : new SourceLine(content, location) { Indent = indent });
        }

        return result;
    }

    private static string StripQuote(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('>'))
        {
            return line;
        }

        while (trimmed.StartsWith('>'))
        {
            trimmed = trimmed[1..];
            if (trimmed.StartsWith(' '))
            {
                trimmed = trimmed[1..];
            }
        }

        return trimmed;
    }
}
