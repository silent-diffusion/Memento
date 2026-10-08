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

    /// <summary>The line without its leading quote marks ("> > text" → "text"), found by one scan rather than by slicing per mark.</summary>
    internal static string StripQuote(string line)
    {
        var start = 0;
        while (start < line.Length && char.IsWhiteSpace(line[start]))
        {
            start++;
        }

        if (start == line.Length || line[start] != '>')
        {
            return line;
        }

        var i = start;
        while (i < line.Length && line[i] == '>')
        {
            i++;
            if (i < line.Length && line[i] == ' ')
            {
                i++;
            }
        }

        return line[i..];
    }

    /// <summary>The text after any number of leading "&gt;" marks, each followed by optional whitespace (Markdown block quotes).</summary>
    internal static string StripQuoteMarks(string text)
    {
        var i = 0;
        while (i < text.Length && text[i] == '>')
        {
            i++;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
        }

        return i == 0 ? text : text[i..];
    }
}
