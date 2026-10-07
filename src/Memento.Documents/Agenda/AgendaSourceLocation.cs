using System.Globalization;
using System.Text;

namespace Memento.Documents.Agenda;

/// <summary>
/// Where an item came from in the source, 1-based. Only the parts that apply to the source are set: a text line, a
/// Word paragraph or table row, a sheet row and cell, a PDF page and line, or a line of an image.
/// </summary>
public sealed record AgendaSourceLocation
{
    public int? Page { get; init; }

    public int? Line { get; init; }

    public int? Paragraph { get; init; }

    public int? Table { get; init; }

    public int? Row { get; init; }

    public string? Sheet { get; init; }

    /// <summary>A spreadsheet cell reference such as <c>B4</c>.</summary>
    public string? Cell { get; init; }

    public static AgendaSourceLocation AtLine(int line) => new() { Line = line };

    /// <summary>A short user-facing description, e.g. "page 2, line 5" or "sheet Agenda, cell B4".</summary>
    public override string ToString()
    {
        var parts = new List<string>(3);
        if (Sheet is not null)
        {
            parts.Add($"sheet {Sheet}");
        }

        if (Page is { } page)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"page {page}"));
        }

        if (Table is { } table)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"table {table}"));
        }

        if (Cell is not null)
        {
            parts.Add($"cell {Cell}");
        }
        else if (Row is { } row)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"row {row}"));
        }

        if (Paragraph is { } paragraph)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"paragraph {paragraph}"));
        }

        if (Line is { } line)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"line {line}"));
        }

        var text = new StringBuilder();
        foreach (var part in parts)
        {
            text.Append(text.Length == 0 ? string.Empty : ", ").Append(part);
        }

        return text.ToString();
    }
}
