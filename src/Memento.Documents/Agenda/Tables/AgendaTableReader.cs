using System.Globalization;
using System.Text.RegularExpressions;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.Tables;

/// <summary>
/// Finds the agenda in a table: detects a header row, picks the column whose header looks like "Item", "Topic" or
/// "Agenda" (or the first text column when there is no header), takes times from a time column and numbers from a
/// number column, and turns rows that span the table into section headings. Ambiguous column choices mark the items.
/// </summary>
internal static partial class AgendaTableReader
{
    /// <summary>The heading level of a section row: below a document's own headings, above headings found in the text.</summary>
    public const int SectionHeadingLevel = 50;

    public static TableReadResult Read(IReadOnlyList<TableRow> rawRows, CancellationToken cancellationToken)
    {
        var rows = rawRows.Select(r => r with { Cells = r.Cells.Select(c => (c ?? string.Empty).Trim()).ToList() }).ToList();
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Cells.Count);
        var lines = new List<SourceLine>();
        var warnings = new List<AgendaParseWarning>();
        if (width == 0)
        {
            return new TableReadResult(lines, warnings, false);
        }

        var headerIndex = FindHeader(rows);
        var header = headerIndex >= 0 ? rows[headerIndex].Cells : null;
        var dataStart = headerIndex + 1;
        var data = rows.Skip(dataStart).ToList();

        // Rows above the header (or a first row with one cell above a wider table): a title and meeting details.
        var preamble = headerIndex >= 0 ? rows.Take(headerIndex).ToList() : LeadingSingleCellRows(rows);
        if (headerIndex < 0)
        {
            data = rows.Skip(preamble.Count).ToList();
        }

        var firstPreamble = true;
        foreach (var row in preamble)
        {
            var text = string.Join(" ", row.Cells.Where(c => c.Length > 0));
            if (text.Length == 0)
            {
                continue;
            }

            lines.Add(new SourceLine(text, row.Location) { IsTitle = firstPreamble && row.Cells.Count(c => c.Length > 0) == 1, MayContinue = false });
            firstPreamble = false;
        }

        var stats = Enumerable.Range(0, width).Select(c => ColumnStats.Of(data, c)).ToList();
        var timeColumn = FindTimeColumn(header, stats);
        var numberColumn = FindNumberColumn(header, stats, timeColumn);
        var (itemColumn, ambiguousWith, guessed, hasAgendaHeader) = ChooseItemColumn(header, stats, timeColumn, numberColumn);
        if (itemColumn < 0)
        {
            return new TableReadResult(lines, warnings, false);
        }

        var itemName = ColumnName(header, itemColumn);
        string? reason = ambiguousWith >= 0
            ? (guessed ? UncertainReasons.ColumnGuessed(itemName) : UncertainReasons.ColumnAmbiguous(itemName, ColumnName(header, ambiguousWith)))
            : null;

        var withoutItem = new List<string>();
        var unused = new List<string>();
        var unusedColumns = new HashSet<int>();
        var previousBlank = false;
        foreach (var row in data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nonEmpty = row.Cells.Select((c, i) => (Text: c, Index: i)).Where(c => c.Text.Length > 0).ToList();
            if (nonEmpty.Count == 0)
            {
                if (!previousBlank)
                {
                    lines.Add(SourceLine.Blank(row.Location));
                    previousBlank = true;
                }

                continue;
            }

            previousBlank = false;
            var item = Cell(row, itemColumn);
            if (item.Length == 0)
            {
                var only = nonEmpty.Count == 1 ? nonEmpty[0] : default;
                // A section row: a merged cell across the row, or a lone label left of the agenda column ("Afternoon").
                if (row.MergedAcross ||
                    (nonEmpty.Count == 1 && only.Index < itemColumn && only.Index != timeColumn && only.Index != numberColumn && !MarkerParser.IsTime(only.Text)))
                {
                    lines.Add(new SourceLine(Flatten(nonEmpty[0].Text), row.LocationOf(nonEmpty[0].Index)) { HeadingLevel = SectionHeadingLevel, MayContinue = false });
                }
                else
                {
                    withoutItem.Add($"{row.Location}: {string.Join(" · ", nonEmpty.Select(c => c.Text))}");
                }

                continue;
            }

            if (row.MergedAcross && nonEmpty.Count == 1)
            {
                lines.Add(new SourceLine(Flatten(item), row.LocationOf(itemColumn)) { HeadingLevel = SectionHeadingLevel, MayContinue = false });
                continue;
            }

            var time = timeColumn >= 0 ? Cell(row, timeColumn) : string.Empty;
            var number = numberColumn >= 0 ? Cell(row, numberColumn) : string.Empty;
            ListMarker? marker = null;
            if (number.Length > 0 && int.TryParse(number.TrimEnd('.', ')'), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                marker = new ListMarker(MarkerStyle.Decimal, value, value.ToString(CultureInfo.InvariantCulture));
            }

            AddCellLines(lines, item, row.LocationOf(itemColumn), time.Length > 0 ? time : null, marker, reason);

            var others = nonEmpty.Where(c => c.Index != itemColumn && c.Index != timeColumn && c.Index != numberColumn).ToList();
            if (others.Count > 0)
            {
                unusedColumns.UnionWith(others.Select(c => c.Index));
                unused.Add($"{Flatten(item)}: {string.Join(" · ", others.Select(c => $"{ColumnName(header, c.Index)} = {Flatten(c.Text)}"))}");
            }
        }

        if (unusedColumns.Count > 0)
        {
            var names = unusedColumns.Order().Select(c => Quote(ColumnName(header, c))).ToList();
            warnings.Add(new AgendaParseWarning(
                AgendaWarningCodes.ColumnsUnused,
                $"Only the {Quote(itemName)} column became agenda items; {JoinNames(names)} {(names.Count == 1 ? "was" : "were")} left out. Add anything you need from {(names.Count == 1 ? "it" : "them")} here.",
                string.Join('\n', unused)));
        }

        if (withoutItem.Count > 0)
        {
            warnings.Add(new AgendaParseWarning(
                AgendaWarningCodes.RowsWithoutItem,
                withoutItem.Count == 1
                    ? $"One row has nothing in the {Quote(itemName)} column, so it was not added. Add it here if it belongs in the agenda."
                    : string.Create(CultureInfo.InvariantCulture, $"{withoutItem.Count} rows have nothing in the {Quote(itemName)} column, so they were not added. Add any that belong in the agenda here."),
                string.Join('\n', withoutItem)));
        }

        return new TableReadResult(lines, warnings, hasAgendaHeader);
    }

    /// <summary>Spreadsheet-style column letters: 0 → A, 26 → AA.</summary>
    public static string ColumnLetter(int index)
    {
        var name = string.Empty;
        index++;
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            name = (char)('A' + remainder) + name;
            index = (index - 1) / 26;
        }

        return name;
    }

    private static void AddCellLines(List<SourceLine> lines, string cell, AgendaSourceLocation location, string? time, ListMarker? marker, string? reason)
    {
        // A cell with several lines: the first is the item, marked lines below it are sub-items, plain ones wrap.
        var parts = TextLines.Split(cell).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var reasons = reason is null ? (IReadOnlyList<string>)[] : [reason];
        var head = parts[0];
        var index = 1;
        while (index < parts.Count && !MarkerParser.TryParseMarker(parts[index], out _, out _))
        {
            head += " " + parts[index];
            index++;
        }

        lines.Add(new SourceLine(head, location) { Time = time, Marker = marker, MayContinue = false, Reasons = reasons });
        for (; index < parts.Count; index++)
        {
            lines.Add(new SourceLine(parts[index], location) { Indent = 4, MayContinue = true, Reasons = reasons });
        }
    }

    private static int FindHeader(List<TableRow> rows)
    {
        var checkedRows = 0;
        for (var i = 0; i < rows.Count && checkedRows < 6; i++)
        {
            var cells = rows[i].Cells.Where(c => c.Length > 0).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            checkedRows++;
            if (cells.Count < 2 && rows.Max(r => r.Cells.Count(c => c.Length > 0)) > 1)
            {
                continue;
            }

            var headerish = cells.Count(c => c.Length <= 30 && !MarkerParser.IsTime(c) && !double.TryParse(c, NumberStyles.Any, CultureInfo.InvariantCulture, out _));
            if (cells.Any(c => HeaderWordPattern().IsMatch(c)) && headerish * 2 >= cells.Count)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<TableRow> LeadingSingleCellRows(List<TableRow> rows)
    {
        var widest = rows.Max(r => r.Cells.Count(c => c.Length > 0));
        if (widest < 2)
        {
            return [];
        }

        var preamble = new List<TableRow>();
        foreach (var row in rows)
        {
            var count = row.Cells.Count(c => c.Length > 0);
            if (count > 1 || preamble.Count >= 3)
            {
                break;
            }

            preamble.Add(row);
        }

        return preamble.Count == rows.Count ? [] : preamble;
    }

    private static int FindTimeColumn(IReadOnlyList<string>? header, List<ColumnStats> stats)
    {
        if (header is not null)
        {
            for (var c = 0; c < header.Count; c++)
            {
                if (TimeHeaderPattern().IsMatch(header[c]))
                {
                    return c;
                }
            }
        }

        var timey = stats.FirstOrDefault(s => s.NonEmpty > 0 && s.Times * 10 >= s.NonEmpty * 6);
        return timey?.Column ?? -1;
    }

    private static int FindNumberColumn(IReadOnlyList<string>? header, List<ColumnStats> stats, int timeColumn)
    {
        if (header is not null)
        {
            for (var c = 0; c < header.Count; c++)
            {
                if (c != timeColumn && NumberHeaderPattern().IsMatch(header[c]))
                {
                    return c;
                }
            }
        }

        // Without a header, only a first column counting up (1, 2, 3) is a number column; durations are not.
        var first = stats[0];
        return timeColumn != 0 && first.NonEmpty > 1 && first.SmallIntegers == first.NonEmpty && first.Increasing ? 0 : -1;
    }

    private static (int Column, int AmbiguousWith, bool Guessed, bool HasAgendaHeader) ChooseItemColumn(
        IReadOnlyList<string>? header, List<ColumnStats> stats, int timeColumn, int numberColumn)
    {
        if (header is not null)
        {
            var scored = header
                .Select((h, c) => (Column: c, Score: HeaderScore(h)))
                .Where(s => s.Score > 0 && s.Column != timeColumn && s.Column != numberColumn && stats[s.Column].NonEmpty > 0)
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Column)
                .ToList();
            if (scored.Count > 0)
            {
                var best = scored[0];
                var rival = scored.Skip(1).FirstOrDefault(s => s.Score >= 6 && s.Score >= best.Score - 1);
                return (best.Column, scored.Count > 1 && rival.Score > 0 ? rival.Column : -1, false, best.Score >= 6);
            }
        }

        // No header naming the agenda: the first text column, flagged when another text column could be it.
        var candidates = stats
            .Where(s => s.Column != timeColumn && s.Column != numberColumn && s.NonEmpty > 0 && s.TextLike * 10 >= s.NonEmpty * 6)
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = stats.Where(s => s.Column != timeColumn && s.Column != numberColumn && s.NonEmpty > 0).ToList();
        }

        if (candidates.Count == 0)
        {
            return (-1, -1, false, false);
        }

        var chosen = candidates[0];
        var other = candidates.Skip(1).FirstOrDefault(s => s.NonEmpty * 2 >= chosen.NonEmpty);
        return (chosen.Column, other?.Column ?? -1, true, false);
    }

    private static int HeaderScore(string header)
    {
        var text = header.Trim().TrimEnd(':', '#').Trim().ToLowerInvariant();
        return text switch
        {
            "agenda item" or "agenda items" or "item" or "items" or "topic" or "topics" or "agenda" or "agenda topic" or "agenda topics" => 10,
            "subject" or "session" or "title" or "activity" or "discussion" or "discussion item" or "discussion topic" or "what" => 8,
            "description" or "details" => 5,
            _ when ItemWordPattern().IsMatch(text) => 7,
            _ when SessionWordPattern().IsMatch(text) => 6,
            _ when text.Contains("description", StringComparison.Ordinal) => 4,
            _ => 0,
        };
    }

    private static string Cell(TableRow row, int column) => column < row.Cells.Count ? row.Cells[column] : string.Empty;

    private static string Flatten(string text) => string.Join(" ", TextLines.Split(text).Select(t => t.Trim()).Where(t => t.Length > 0));

    private static string ColumnName(IReadOnlyList<string>? header, int column) =>
        header is not null && column < header.Count && header[column].Length > 0 ? header[column] : $"column {ColumnLetter(column)}";

    private static string Quote(string name) => name.StartsWith("column ", StringComparison.Ordinal) ? name : $"\"{name}\"";

    private static string JoinNames(List<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    [GeneratedRegex(@"\b(?:item|items|topic|topics|agenda|subject|session|title|activity|discussion|description|details|time|start|end|duration|minutes|mins|owner|lead|led by|presenter|speaker|who|notes|no\.?|number|slot|when|outcome)\b|^#$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderWordPattern();

    [GeneratedRegex(@"\b(?:item|items|topic|topics|agenda)\b")]
    private static partial Regex ItemWordPattern();

    [GeneratedRegex(@"\b(?:subject|session|title|activity|discussion)\b")]
    private static partial Regex SessionWordPattern();

    [GeneratedRegex(@"^\s*(?:time|times|start|start time|starts|when|slot|time slot|from|begins?)\s*:?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeHeaderPattern();

    [GeneratedRegex(@"^\s*(?:#|no\.?|nr\.?|num|number|item\s*#|item no\.?|ref)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberHeaderPattern();

    private sealed record ColumnStats(int Column, int NonEmpty, int TextLike, int Times, int SmallIntegers, bool Increasing)
    {
        public static ColumnStats Of(List<TableRow> rows, int column)
        {
            int nonEmpty = 0, textLike = 0, times = 0, integers = 0, previous = int.MinValue;
            var increasing = true;
            foreach (var row in rows)
            {
                var cell = Cell(row, column);
                if (cell.Length == 0)
                {
                    continue;
                }

                nonEmpty++;
                if (MarkerParser.IsTime(cell))
                {
                    times++;
                }
                else if (int.TryParse(cell.TrimEnd('.', ')'), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < 1000)
                {
                    integers++;
                    increasing &= n > previous;
                    previous = n;
                }
                else if (cell.Count(char.IsLetter) >= 3)
                {
                    textLike++;
                }
            }

            return new ColumnStats(column, nonEmpty, textLike, times, integers, increasing);
        }
    }
}
