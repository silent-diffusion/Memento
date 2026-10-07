using System.Globalization;
using System.IO.Packaging;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Memento.Documents.Agenda.Tables;
using AgendaTableRow = Memento.Documents.Agenda.Tables.TableRow;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.OpenXml;

/// <summary>
/// Word documents: automatic numbering (<c>w:numPr</c>, direct or from a list style), heading and title styles, typed
/// numbers and bullets, tables with an agenda-like column. Text boxes are not read as items; their text is reported
/// in a warning.
/// </summary>
public sealed class DocxAgendaParser : IAgendaParser
{
    public IReadOnlyList<AgendaSourceKind> Kinds { get; } = [AgendaSourceKind.Docx];

    public bool CanParse(string fileName, string? contentType) =>
        AgendaResults.HasExtension(fileName, ".docx", ".docm", ".dotx") ||
        AgendaResults.HasContentType(contentType, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

    public async Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await AgendaContent.ReadAsync(content, options, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => Parse(bytes, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static AgendaParseResult Parse(ReadOnlyMemory<byte> bytes, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        WordprocessingDocument document;
        try
        {
            document = WordprocessingDocument.Open(stream, false, new OpenSettings { AutoSave = false });
        }
        catch (Exception e) when (e is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException)
        {
            throw AgendaErrors.Unreadable(options, "a Word document", e);
        }

        using (document)
        {
            var body = document.MainDocumentPart?.Document?.Body ?? throw AgendaErrors.Unreadable(options, "a Word document");
            var styles = new WordStyles(document.MainDocumentPart!.StyleDefinitionsPart?.Styles);
            var numbering = new WordNumbering(document.MainDocumentPart.NumberingDefinitionsPart?.Numbering, styles);
            var reader = new BodyReader(styles, numbering, cancellationToken);
            reader.ReadBlocks(body);

            var warnings = new List<AgendaParseWarning>();
            var lines = reader.Flatten(warnings);
            var textBoxes = TextBoxTexts(body);
            if (textBoxes.Count > 0)
            {
                warnings.Add(new AgendaParseWarning(
                    AgendaWarningCodes.TextBoxesIgnored,
                    textBoxes.Count == 1
                        ? "The document has a text box, which was not read as agenda items. Add anything from it that belongs in the agenda here."
                        : string.Create(CultureInfo.InvariantCulture, $"The document has {textBoxes.Count} text boxes, which were not read as agenda items. Add anything from them that belongs in the agenda here."),
                    string.Join('\n', textBoxes)));
            }

            var structured = AgendaStructurer.Structure(lines, cancellationToken);
            return AgendaResults.Create(AgendaSourceKind.Docx, options, structured, warnings);
        }
    }

    private static List<string> TextBoxTexts(Body body)
    {
        // Word writes each text box twice (DrawingML and a VML fallback); read the first copy only.
        return body.Descendants<TextBoxContent>()
            .Where(t => !t.Ancestors<AlternateContentFallback>().Any() && !t.Ancestors<TextBoxContent>().Any())
            .Select(t => string.Join(" ", t.Descendants<Paragraph>().Select(p => ParagraphText(p, includeTextBoxes: true).Trim()).Where(s => s.Length > 0)))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The visible text of a paragraph: runs, tabs, soft line breaks as \n; no deleted text, field codes or text boxes.</summary>
    internal static string ParagraphText(OpenXmlElement paragraph, bool includeTextBoxes = false)
    {
        var text = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (element is not (WordText or TabChar or Break or CarriageReturn or NoBreakHyphen))
            {
                continue;
            }

            if (element.Ancestors<AlternateContentFallback>().Any() ||
                (!includeTextBoxes && element.Ancestors<TextBoxContent>().Any()) ||
                element.Ancestors<DeletedRun>().Any())
            {
                continue;
            }

            switch (element)
            {
                case WordText t:
                    text.Append(t.Text);
                    break;
                case TabChar:
                    text.Append('\t');
                    break;
                case Break br when br.Type?.Value != BreakValues.Page && br.Type?.Value != BreakValues.Column:
                    text.Append('\n');
                    break;
                case Break:
                    break;
                case CarriageReturn:
                    text.Append('\n');
                    break;
                case NoBreakHyphen:
                    text.Append('-');
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>Walks the body in order, keeping paragraphs as lines and tables for a decision once the whole body is known.</summary>
    private sealed class BodyReader(WordStyles styles, WordNumbering numbering, CancellationToken cancellationToken)
    {
        private readonly List<Block> _blocks = [];
        private int _paragraphs;
        private int _tables;

        public void ReadBlocks(OpenXmlElement container)
        {
            foreach (var element in container.ChildElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (element)
                {
                    case Paragraph paragraph:
                        _blocks.Add(new Block(ReadParagraph(paragraph), null, 0));
                        break;
                    case Table table:
                        _tables++;
                        _blocks.Add(new Block(null, ReadTable(table, _tables), _tables));
                        break;
                    case SdtBlock sdt when sdt.SdtContentBlock is { } sdtContent:
                        ReadBlocks(sdtContent);
                        break;
                    case CustomXmlBlock custom:
                        ReadBlocks(custom);
                        break;
                }
            }
        }

        public List<SourceLine> Flatten(List<AgendaParseWarning> warnings)
        {
            var paragraphContent = _blocks
                .Where(b => b.Lines is not null)
                .SelectMany(b => b.Lines!)
                .Count(l => !l.IsBlank && l.HeadingLevel is null && !l.IsTitle);
            var lines = new List<SourceLine>();
            foreach (var block in _blocks)
            {
                if (block.Lines is not null)
                {
                    lines.AddRange(block.Lines);
                    continue;
                }

                var table = AgendaTableReader.Read(block.Rows!, cancellationToken);
                if (table.HasAgendaHeader || paragraphContent < 2)
                {
                    lines.Add(SourceLine.Blank(new AgendaSourceLocation { Table = block.TableNumber }));
                    lines.AddRange(table.Lines.Select(l => l with { IsTitle = false }));
                    lines.Add(SourceLine.Blank(new AgendaSourceLocation { Table = block.TableNumber }));
                    warnings.AddRange(table.Warnings);
                }
                else
                {
                    var text = block.Rows!.Select(r => string.Join(" · ", r.Cells.Select(c => c.Replace('\n', ' ').Trim()).Where(c => c.Length > 0))).Where(r => r.Length > 0).ToList();
                    if (text.Count > 0)
                    {
                        warnings.Add(new AgendaParseWarning(
                            AgendaWarningCodes.TableSkipped,
                            string.Create(CultureInfo.InvariantCulture, $"Table {block.TableNumber} does not look like part of the agenda, so it was not read as items. Add anything from it that belongs in the agenda here."),
                            string.Join('\n', text),
                            new AgendaSourceLocation { Table = block.TableNumber }));
                    }
                }
            }

            return lines;
        }

        private List<SourceLine> ReadParagraph(Paragraph paragraph)
        {
            _paragraphs++;
            var location = new AgendaSourceLocation { Paragraph = _paragraphs };
            var text = ParagraphText(paragraph);
            if (text.Trim().Length == 0)
            {
                return [SourceLine.Blank(location)];
            }

            var heading = styles.HeadingLevel(paragraph);
            var isTitle = styles.IsTitle(paragraph);
            var numbered = numbering.Resolve(paragraph);
            var indent = numbered is { } n ? n.Level * 4 : LeftIndent(paragraph);
            ListMarker? marker = numbered?.Marker;
            if (numbered is null && heading is null && !isTitle && styles.ListStyleLevel(paragraph) is { } styleLevel)
            {
                // A list style whose bullet or number lives in the style sheet Word did not save with it.
                indent = styleLevel * 4;
                if (styles.IsBulletStyle(paragraph))
                {
                    marker = new ListMarker(MarkerStyle.Bullet, null, null, BulletFamily: styleLevel % 3);
                }
            }

            var parts = TextLines.Split(text).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (heading is not null || isTitle)
            {
                // A heading with a soft line break inside: the heading and the next item may have run together.
                return
                [
                    new SourceLine(string.Join(" ", parts), location)
                    {
                        HeadingLevel = isTitle ? null : heading,
                        IsTitle = isTitle,
                        HeadingMergeSuspected = parts.Count > 1,
                    },
                ];
            }

            var lines = new List<SourceLine>
            {
                new(parts[0], location) { Indent = indent, Marker = marker },
            };
            foreach (var part in parts.Skip(1))
            {
                lines.Add(new SourceLine(part, location) { Indent = indent + 2 });
            }

            return lines;
        }

        private static int LeftIndent(Paragraph paragraph)
        {
            var indentation = paragraph.ParagraphProperties?.Indentation;
            var value = indentation?.Left?.Value ?? indentation?.Start?.Value;
            return value is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twips) && twips > 0
                ? twips / 180
                : 0;
        }

        private static List<AgendaTableRow> ReadTable(Table table, int number)
        {
            var rows = new List<AgendaTableRow>();
            var gridColumns = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Count() ?? 0;
            var rowNumber = 0;
            foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
            {
                rowNumber++;
                var cells = new List<string>();
                var cellElements = row.Elements<TableCell>().ToList();
                foreach (var cell in cellElements)
                {
                    var continuation = cell.TableCellProperties?.VerticalMerge is { } merge && merge.Val?.Value != MergedCellValues.Restart;
                    var text = continuation
                        ? string.Empty
                        : string.Join('\n', cell.Descendants<Paragraph>()
                            .Where(p => !p.Ancestors<TextBoxContent>().Any())
                            .Select(p => ParagraphText(p).Trim())
                            .Where(t => t.Length > 0));
                    cells.Add(text);
                    var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                    for (var s = 1; s < span; s++)
                    {
                        cells.Add(string.Empty);
                    }
                }

                var spansAll = cellElements.Count == 1 && (gridColumns > 1 || (cellElements[0].TableCellProperties?.GridSpan?.Val?.Value ?? 1) > 1);
                rows.Add(new AgendaTableRow(cells, new AgendaSourceLocation { Table = number, Row = rowNumber }) { MergedAcross = spansAll });
            }

            return rows;
        }
    }

    private sealed record Block(List<SourceLine>? Lines, List<AgendaTableRow>? Rows, int TableNumber);
}
