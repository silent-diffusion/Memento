using System.Globalization;
using System.IO.Packaging;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Memento.Documents.Agenda.Tables;
using Memento.Documents.Agenda.Text;
using AgendaTableRow = Memento.Documents.Agenda.Tables.TableRow;

namespace Memento.Documents.Agenda.OpenXml;

/// <summary>
/// Excel workbooks: the sheet named like "Agenda" (or the first sheet with data), shared and inline strings, times and
/// dates shown as Excel shows them, merged cells that span a row as section headings, and the agenda-like column.
/// </summary>
public sealed partial class XlsxAgendaParser : IAgendaParser
{
    private const int MaxRows = 10_000;

    /// <summary>Columns read from a sheet; an agenda never needs more, and the table is built densely.</summary>
    private const int MaxColumns = 64;

    /// <summary>Excel's last column, XFD.</summary>
    private const int ExcelColumns = 16_384;

    public IReadOnlyList<AgendaSourceKind> Kinds { get; } = [AgendaSourceKind.Xlsx];

    public bool CanParse(string fileName, string? contentType) =>
        AgendaResults.HasExtension(fileName, ".xlsx", ".xlsm", ".xltx") ||
        AgendaResults.HasContentType(contentType, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

    public async Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await AgendaContent.ReadAsync(content, options, cancellationToken).ConfigureAwait(false);
        return await ParseGuard.RunAsync(options, "an Excel workbook", token => Parse(bytes, options, token), cancellationToken).ConfigureAwait(false);
    }

    private static AgendaParseResult Parse(ReadOnlyMemory<byte> bytes, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        PackageGuard.Check(stream, options, "an Excel workbook", cancellationToken);
        SpreadsheetDocument document;
        try
        {
            document = SpreadsheetDocument.Open(stream, false, PackageGuard.OpenSettings());
        }
        catch (Exception e) when (e is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException)
        {
            throw AgendaErrors.Unreadable(options, "an Excel workbook", e);
        }

        using (document)
        {
            var workbookPart = document.WorkbookPart ?? throw AgendaErrors.Unreadable(options, "an Excel workbook");
            var context = new SheetContext(workbookPart);
            var sheets = (workbookPart.Workbook?.Sheets?.Elements<Sheet>() ?? [])
                .Where(s => OpenXmlValues.Enum(s.State) is not { } state || state == SheetStateValues.Visible)
                .Select(s => (Sheet: s, Rows: ReadSheet(workbookPart, s, context, cancellationToken)))
                .ToList();
            var withData = sheets.Where(s => s.Rows.Any(r => r.Cells.Any(c => c.Length > 0))).ToList();
            if (withData.Count == 0)
            {
                throw AgendaErrors.NoItems(options);
            }

            var chosen = withData.FirstOrDefault(s => RegexGuard.IsMatch(AgendaNamePattern(), s.Sheet.Name?.Value ?? string.Empty));
            if (chosen.Sheet is null)
            {
                chosen = withData[0];
            }

            var warnings = new List<AgendaParseWarning>();
            var name = chosen.Sheet.Name?.Value ?? "Sheet1";
            if (withData.Count > 1)
            {
                warnings.Add(new AgendaParseWarning(
                    AgendaWarningCodes.SheetChosen,
                    string.Create(CultureInfo.InvariantCulture, $"The workbook has {withData.Count} sheets with content; the agenda was read from the \"{name}\" sheet. If another sheet holds the agenda, rename it to \"Agenda\" or save it on its own, then import again."),
                    string.Join('\n', withData.Select(s => s.Sheet.Name?.Value ?? string.Empty)),
                    new AgendaSourceLocation { Sheet = name }));
            }

            var table = AgendaTableReader.Read(chosen.Rows, cancellationToken);
            warnings.AddRange(table.Warnings);
            var structured = AgendaStructurer.Structure(table.Lines, cancellationToken);
            return AgendaResults.Create(AgendaSourceKind.Xlsx, options, structured, warnings);
        }
    }

    private static List<AgendaTableRow> ReadSheet(WorkbookPart workbookPart, Sheet sheet, SheetContext context, CancellationToken cancellationToken)
    {
        var name = sheet.Name?.Value ?? string.Empty;
        if (sheet.Id?.Value is not { } id || !workbookPart.TryGetPartById(id, out var sheetPart) || sheetPart is not WorksheetPart part)
        {
            return [];
        }

        var worksheet = part.Worksheet;
        var data = worksheet?.GetFirstChild<SheetData>();
        if (data is null)
        {
            return [];
        }

        // Rows and columns past the limits are skipped, not stored: a damaged sheet can name row 4294967295 or column
        // ZZZZZZ, and the table below is built densely from the first to the last row.
        var cellsByRow = new SortedDictionary<int, Dictionary<int, string>>();
        long nextRow = 1;
        foreach (var row in data.Elements<Row>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            long rowIndex = OpenXmlValues.UInt(row.RowIndex) ?? nextRow;
            nextRow = rowIndex + 1;
            if (rowIndex is 0 or > MaxRows)
            {
                continue;
            }

            var cells = new Dictionary<int, string>();
            var nextColumn = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                var column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : nextColumn;
                if (column is < 0 or >= MaxColumns)
                {
                    nextColumn = MaxColumns;
                    continue;
                }

                nextColumn = column + 1;
                var value = context.ValueOf(cell);
                if (value.Length > 0)
                {
                    cells[column] = value;
                }
            }

            if (cells.Count > 0)
            {
                cellsByRow[(int)rowIndex] = cells;
            }
        }

        if (cellsByRow.Count == 0)
        {
            return [];
        }

        var mergedRows = MergedAcrossRows(worksheet!, cellsByRow);
        var width = cellsByRow.Values.Max(c => c.Keys.Max()) + 1;
        var rows = new List<AgendaTableRow>();
        for (var r = cellsByRow.Keys.First(); r <= cellsByRow.Keys.Last(); r++)
        {
            var values = cellsByRow.TryGetValue(r, out var found) ? found : [];
            var cells = Enumerable.Range(0, width).Select(c => values.TryGetValue(c, out var v) ? v : string.Empty).ToList();
            var rowNumber = r;
            rows.Add(new AgendaTableRow(cells, new AgendaSourceLocation { Sheet = name, Row = rowNumber })
            {
                MergedAcross = mergedRows.Contains(rowNumber),
                CellLocation = c => new AgendaSourceLocation { Sheet = name, Cell = AgendaTableReader.ColumnLetter(c) + rowNumber.ToString(CultureInfo.InvariantCulture) },
            });
        }

        return rows;
    }

    private static HashSet<int> MergedAcrossRows(Worksheet worksheet, SortedDictionary<int, Dictionary<int, string>> cellsByRow)
    {
        // A horizontal merge that holds every filled cell of its row is a section row ("Afternoon session").
        var rows = new HashSet<int>();
        foreach (var merge in worksheet.Elements<MergeCells>().SelectMany(m => m.Elements<MergeCell>()))
        {
            var reference = merge.Reference?.Value;
            var parts = reference?.Split(':');
            if (parts is not { Length: 2 })
            {
                continue;
            }

            var (firstColumn, firstRow) = (ColumnIndex(parts[0]), RowIndex(parts[0]));
            var (lastColumn, lastRow) = (ColumnIndex(parts[1]), RowIndex(parts[1]));
            if (firstColumn < 0 || firstRow != lastRow || lastColumn <= firstColumn || !cellsByRow.TryGetValue(firstRow, out var cells))
            {
                continue;
            }

            if (cells.Keys.All(c => c >= firstColumn && c <= lastColumn))
            {
                rows.Add(firstRow);
            }
        }

        return rows;
    }

    /// <summary>The 0-based column of a reference such as "B7"; -1 past Excel's last column (XFD) or for more than three letters.</summary>
    internal static int ColumnIndex(string reference)
    {
        var index = 0;
        var letters = 0;
        foreach (var c in reference)
        {
            if (!char.IsAsciiLetter(c))
            {
                break;
            }

            if (++letters > 3)
            {
                return -1;
            }

            index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index > ExcelColumns ? -1 : Math.Max(0, index - 1);
    }

    private static int RowIndex(string reference)
    {
        var digits = new string(reference.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var row) ? row : 0;
    }

    [GeneratedRegex(@"agenda|programme|program|schedule|run of show|order of business", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex AgendaNamePattern();

    /// <summary>Shared strings and number formats, read once per workbook.</summary>
    private sealed partial class SheetContext
    {
        private readonly List<string> _sharedStrings;
        private readonly List<uint> _cellFormats;
        private readonly Dictionary<uint, string> _customFormats;

        public SheetContext(WorkbookPart workbookPart)
        {
            _sharedStrings = (workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>() ?? [])
                .Select(ItemText)
                .ToList();
            var stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
            _cellFormats = (stylesheet?.CellFormats?.Elements<CellFormat>() ?? [])
                .Select(f => OpenXmlValues.UInt(f.NumberFormatId) ?? 0)
                .ToList();
            _customFormats = (stylesheet?.NumberingFormats?.Elements<NumberingFormat>() ?? [])
                .Where(f => OpenXmlValues.UInt(f.NumberFormatId) is not null)
                .GroupBy(f => OpenXmlValues.UInt(f.NumberFormatId)!.Value)
                .ToDictionary(g => g.Key, g => g.First().FormatCode?.Value ?? string.Empty);
        }

        public string ValueOf(Cell cell)
        {
            var raw = cell.CellValue?.Text ?? string.Empty;
            var type = OpenXmlValues.Enum(cell.DataType);
            if (type == CellValues.SharedString)
            {
                return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < _sharedStrings.Count
                    ? _sharedStrings[index]
                    : string.Empty;
            }

            // An inline string is read whatever the cell's type says (a type Excel does not know reads as absent).
            if (type == CellValues.InlineString || (cell.InlineString is not null && cell.CellValue is null))
            {
                return cell.InlineString is { } inline ? ItemText(inline) : raw;
            }

            if (type == CellValues.Boolean)
            {
                return raw == "1" ? "TRUE" : "FALSE";
            }

            if (type is null || type == CellValues.Number)
            {
                return FormatNumber(raw, OpenXmlValues.UInt(cell.StyleIndex));
            }

            return raw;
        }

        private static string ItemText(OpenXmlElement item)
        {
            // Plain and rich-text runs, not phonetic guides.
            var text = new StringBuilder();
            foreach (var t in item.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>())
            {
                if (!t.Ancestors<PhoneticRun>().Any())
                {
                    text.Append(t.Text);
                }
            }

            return text.ToString();
        }

        private string FormatNumber(string raw, uint? styleIndex)
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return raw;
            }

            var formatId = styleIndex is { } s && s < _cellFormats.Count ? _cellFormats[(int)s] : 0u;
            var code = _customFormats.TryGetValue(formatId, out var custom) ? custom : string.Empty;
            var kind = FormatKind(formatId, code);
            if (kind == NumberKind.Time && number is >= 0 and < 2958466)
            {
                var time = TimeSpan.FromDays(number - Math.Floor(number));
                var minutes = (int)Math.Round(time.TotalMinutes) % (24 * 60);
                var hours = minutes / 60;
                if (code.Contains("AM/PM", StringComparison.OrdinalIgnoreCase) || formatId is 18 or 19)
                {
                    var twelve = hours % 12 == 0 ? 12 : hours % 12;
                    return string.Create(CultureInfo.InvariantCulture, $"{twelve}:{minutes % 60:00} {(hours < 12 ? "AM" : "PM")}");
                }

                return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes % 60:00}");
            }

            if (kind == NumberKind.Date && number is >= 1 and < 2958466)
            {
                var date = DateTime.FromOADate(number);
                return date.TimeOfDay == TimeSpan.Zero
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            return number.ToString("G15", CultureInfo.InvariantCulture);
        }

        private static NumberKind FormatKind(uint formatId, string code)
        {
            if (formatId is 18 or 19 or 20 or 21 or 45 or 46 or 47)
            {
                return NumberKind.Time;
            }

            if (formatId is >= 14 and <= 17 or 22)
            {
                return NumberKind.Date;
            }

            if (code.Length == 0)
            {
                return NumberKind.Number;
            }

            var bare = QuotedPattern().Replace(code, string.Empty).ToLowerInvariant();
            var hasDate = bare.Contains('d') || bare.Contains('y');
            var hasTime = bare.Contains('h') || bare.Contains('s');
            return hasDate ? NumberKind.Date : hasTime ? NumberKind.Time : NumberKind.Number;
        }

        [GeneratedRegex(@"""[^""]*""|\[[^\]]*\]|\\.", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
        private static partial Regex QuotedPattern();
    }

    private enum NumberKind
    {
        Number,
        Time,
        Date,
    }
}
