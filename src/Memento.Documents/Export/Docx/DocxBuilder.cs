using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;
using Memento.Documents.Styling;
using Document = Memento.Documents.Model.Document;
using ListItem = Memento.Documents.Model.Blocks.ListItem;
using Run = Memento.Documents.Model.Run;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Memento.Documents.Export.Docx;

/// <summary>Builds one Word document from a <see cref="Document"/> and a <see cref="DocumentStyle"/>.</summary>
internal sealed class DocxBuilder
{
    private static readonly string NoBreakSpace = ((char)0x00A0).ToString();

    private readonly Document _document;
    private readonly DocumentStyle _style;
    private readonly ModuleCatalog _catalog;
    private readonly double _base;
    private readonly string _head;
    private readonly List<W.Footnote> _footnotes = [];
    private readonly List<NumberingInstance> _numbering = [];
    private int _nextNumId = 3;

    public DocxBuilder(Document document, DocumentStyle style, ModuleCatalog catalog)
    {
        _document = document;
        _style = style;
        _catalog = catalog;
        _base = style.PrintBasePt();
        _head = DocxUnits.Hex(style.HeadingHex());
    }

    public byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>("rIdStyles").Styles = DocxStyleSheet.Build(_style);
            main.AddNewPart<DocumentSettingsPart>("rIdSettings").Settings = new Settings(
                new FootnoteDocumentWideProperties(new FootnoteSpecialReference { Id = -1 }, new FootnoteSpecialReference { Id = 0 }),
                new Compatibility(new CompatibilitySetting { Name = CompatSettingNameValues.CompatibilityMode, Uri = "http://schemas.microsoft.com/office/word", Val = "15" }));

            var body = new Body();
            body.Append(StyledParagraph(DocxStyleIds.Title, TextRun(_document.Title, RunFormat.Plain)));
            var meta = MetaLine.Format(_document.Meta);
            if (meta.Length > 0)
            {
                body.Append(StyledParagraph(DocxStyleIds.Subtitle, TextRun(meta, RunFormat.Plain)));
            }

            var rows = _document.Rows.Where(r => r.Modules.Count > 0).ToList();
            var contentWidth = DocxStyleSheet.ContentWidthTwips(_style);
            OpenXmlElement? previous = null;
            for (var r = 0; r < rows.Count; r++)
            {
                var ruled = _style.LinesBetweenSections && r > 0;
                if (rows[r].Modules.Count == 1)
                {
                    var elements = Module(rows[r].Modules[0], contentWidth, inCell: false, ruled);
                    body.Append(elements);
                    previous = elements[^1];
                }
                else
                {
                    if (previous is W.Table || ruled)
                    {
                        body.Append(Spacer(ruled ? _style.SpacingPt() : 0));
                    }

                    var table = ColumnsTable(rows[r], contentWidth, ruled);
                    body.Append(table);
                    previous = table;
                }
            }

            if (previous is W.Table)
            {
                body.Append(Spacer(0));
            }

            body.Append(SectionProperties(main));
            main.Document = new W.Document(body);

            if (_footnotes.Count > 0)
            {
                var footnotes = new Footnotes(
                    new W.Footnote(new Paragraph(new W.Run(new SeparatorMark()))) { Type = FootnoteEndnoteValues.Separator, Id = -1 },
                    new W.Footnote(new Paragraph(new W.Run(new ContinuationSeparatorMark()))) { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
                footnotes.Append(_footnotes);
                main.AddNewPart<FootnotesPart>("rIdFootnotes").Footnotes = footnotes;
            }

            var numbering = new Numbering(DocxStyleSheet.AbstractNumbers());
            numbering.Append(new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = DocxStyleSheet.BulletNumberingId });
            numbering.Append(new NumberingInstance(new AbstractNumId { Val = 3 }) { NumberID = DocxStyleSheet.HeadingNumberingId });
            numbering.Append(_numbering);
            main.AddNewPart<NumberingDefinitionsPart>("rIdNumbering").Numbering = numbering;

            package.PackageProperties.Title = _document.Title;
            if (_document.CreatedAt != default)
            {
                package.PackageProperties.Created = _document.CreatedAt.UtcDateTime;
            }

            if (_document.ModifiedAt != default)
            {
                package.PackageProperties.Modified = _document.ModifiedAt.UtcDateTime;
            }
        }

        return stream.ToArray();
    }

    private SectionProperties SectionProperties(MainDocumentPart main)
    {
        var (width, height) = StyleMetrics.PaperTwips(_style.Paper);
        var section = new SectionProperties();
        if (_style.RunningHeader)
        {
            var (left, right) = MetaLine.RunningHeader(_document);
            var headerPart = main.AddNewPart<HeaderPart>("rIdHeader");
            var paragraph = StyledParagraph(DocxStyleIds.Header, TextRun(left, RunFormat.Plain));
            if (right.Length > 0)
            {
                paragraph.Append(new W.Run(new TabChar()), TextRun(right, RunFormat.Plain));
            }

            headerPart.Header = new Header(paragraph);
            section.Append(new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(headerPart) });
        }

        if (_style.PageNumbers)
        {
            var footerPart = main.AddNewPart<FooterPart>("rIdFooter");
            footerPart.Footer = new Footer(new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = DocxStyleIds.Footer }),
                new SimpleField(new W.Run(new Text("1"))) { Instruction = " PAGE " },
                new W.Run(new Text(" of ") { Space = SpaceProcessingModeValues.Preserve }),
                new SimpleField(new W.Run(new Text("1"))) { Instruction = " NUMPAGES " }));
            section.Append(new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footerPart) });
        }

        section.Append(new W.PageSize { Width = width, Height = height });
        section.Append(new PageMargin
        {
            Top = DocxUnits.InchesToTwips(StyleMetrics.MarginTopInches),
            Bottom = DocxUnits.InchesToTwips(StyleMetrics.MarginBottomInches),
            Left = (uint)DocxUnits.InchesToTwips(StyleMetrics.MarginSideInches),
            Right = (uint)DocxUnits.InchesToTwips(StyleMetrics.MarginSideInches),
            Header = (uint)DocxUnits.InchesToTwips(StyleMetrics.HeaderFooterInches),
            Footer = (uint)DocxUnits.InchesToTwips(StyleMetrics.HeaderFooterInches),
            Gutter = 0,
        });
        return section;
    }

    /// <summary>Side-by-side modules: a borderless table with one equal column per module (DESIGN.md §12: 28 px gap).</summary>
    private W.Table ColumnsTable(DocumentRow row, int contentWidth, bool ruled)
    {
        var count = row.Modules.Count;
        var gap = DocxUnits.Twips(21);
        var columnWidth = contentWidth / count;
        var borders = new TableBorders(
            ruled ? new TopBorder { Val = BorderValues.Single, Size = 4, Space = 0, Color = DocxUnits.Hex(StyleMetrics.Hairline) } : new TopBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.None },
            new RightBorder { Val = BorderValues.None },
            new InsideHorizontalBorder { Val = BorderValues.None },
            new InsideVerticalBorder { Val = BorderValues.None });
        var table = new W.Table(new TableProperties(
            new TableWidth { Width = contentWidth.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
            new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa },
            borders,
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 0, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = 0, Type = TableWidthValues.Dxa }),
            new TableLook { Val = "0000", FirstRow = false, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true }));
        var grid = new TableGrid();
        for (var i = 0; i < count; i++)
        {
            grid.Append(new GridColumn { Width = columnWidth.ToString(CultureInfo.InvariantCulture) });
        }

        table.Append(grid);
        var tableRow = new W.TableRow();
        for (var i = 0; i < count; i++)
        {
            var left = i > 0 ? gap / 2 : 0;
            var right = i < count - 1 ? gap / 2 : 0;
            var cell = new W.TableCell(new TableCellProperties(
                new TableCellWidth { Width = columnWidth.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
                new TableCellMargin(
                    new LeftMargin { Width = left.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
                    new RightMargin { Width = right.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa })));
            cell.Append(Module(row.Modules[i], columnWidth - left - right, inCell: true, ruled: false));
            tableRow.Append(cell);
        }

        table.Append(tableRow);
        return table;
    }

    private List<OpenXmlElement> Module(ModuleBlock module, int width, bool inCell, bool ruled)
    {
        var factor = module.TextSize.Factor();
        var title = string.IsNullOrWhiteSpace(module.Title) ? _catalog.Find(module.Type)?.DisplayName ?? string.Empty : module.Title;
        var elements = new List<OpenXmlElement>();
        var heading = StyledParagraph(DocxStyleIds.Heading1, TextRun(title, RunFormat.Plain));
        if (ruled)
        {
            var gap = (uint)Math.Min(31, Math.Round(_style.SpacingPt()));
            heading.ParagraphProperties!.Append(new ParagraphBorders(new TopBorder { Val = BorderValues.Single, Size = 4, Space = gap, Color = DocxUnits.Hex(StyleMetrics.Hairline) }));
        }

        elements.Add(heading);
        foreach (var block in module.Blocks)
        {
            elements.AddRange(WriteBlock(block, factor, width));
        }

        if (inCell && elements[^1] is W.Table)
        {
            elements.Add(Spacer(0));
        }

        return elements;
    }

    private IEnumerable<OpenXmlElement> WriteBlock(Block block, double factor, int width)
    {
        var body = new RunFormat { Size = Size(factor) };
        switch (block)
        {
            case HeadingBlock heading:
                var id = Math.Clamp(heading.Level, 1, 3) switch { 1 => DocxStyleIds.Heading2, 2 => DocxStyleIds.Heading3, _ => DocxStyleIds.Heading4 };
                yield return StyledParagraph(id, Runs(heading.Runs, RunFormat.Plain));
                break;
            case ParagraphBlock paragraph:
                yield return StyledParagraph(DocxStyleIds.Normal, Runs(paragraph.Runs, body));
                break;
            case ListBlock list:
                var numId = DocxStyleSheet.BulletNumberingId;
                if (list.Style == ListStyle.Numbered)
                {
                    numId = _nextNumId++;
                    var instance = new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = numId };
                    for (var level = 0; level < 3; level++)
                    {
                        instance.Append(new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = level });
                    }

                    _numbering.Add(instance);
                }

                foreach (var paragraph in ListItems(list.Items, numId, 0, body))
                {
                    yield return paragraph;
                }

                break;
            case TableBlock table:
                yield return Table(table, factor, width);
                break;
            case ChipsBlock chips:
                var chipFormat = new RunFormat { Size = Size(factor * 0.92), Bold = true, Fill = DocxUnits.Hex(StyleMetrics.ChipFill), Padded = true };
                var runs = new List<OpenXmlElement>();
                foreach (var chip in chips.Items)
                {
                    if (runs.Count > 0)
                    {
                        runs.Add(TextRun("  ", new RunFormat { Size = Size(factor * 0.92) }));
                    }

                    runs.Add(TextRun(chip.Replace(" ", NoBreakSpace, StringComparison.Ordinal), chipFormat));
                }

                yield return StyledParagraph(DocxStyleIds.Normal, runs);
                break;
            case LabelValueBlock pairs:
                foreach (var pair in pairs.Pairs)
                {
                    var p = StyledParagraph(DocxStyleIds.Normal, [TextRun(pair.Label, body with { Bold = true }), new W.Run(new TabChar()), .. Runs(pair.Runs, body)]);
                    p.ParagraphProperties!.Append(
                        new Tabs(new TabStop { Val = TabStopValues.Left, Position = 1200 }),
                        new Indentation { Left = "1200", Hanging = "1200" });
                    Reorder(p.ParagraphProperties!);
                    yield return p;
                }

                break;
            case QuoteBlock quote:
                yield return StyledParagraph(DocxStyleIds.Quote, Runs(quote.Runs, body));
                var attribution = new List<string>();
                if (!string.IsNullOrWhiteSpace(quote.Attribution))
                {
                    attribution.Add(quote.Attribution!);
                }

                if (quote.T is { } t)
                {
                    attribution.Add(Timecode.Format(t));
                }

                if (attribution.Count > 0)
                {
                    yield return StyledParagraph(DocxStyleIds.Quote,
                        TextRun("— " + string.Join(MetaLine.Separator, attribution), new RunFormat { Size = Size(factor * 0.85), Color = DocxUnits.Hex(StyleMetrics.MetaInk) }));
                }

                break;
            case TimelineBlock timeline:
                foreach (var entry in timeline.Entries)
                {
                    var p = StyledParagraph(DocxStyleIds.Normal,
                    [
                        TextRun(Timecode.Format(entry.T), new RunFormat { Font = StyleMetrics.MonoWordFont, Size = Size(factor * 0.85), Color = DocxUnits.Hex(StyleMetrics.ChipInk) }),
                        new W.Run(new TabChar()),
                        .. Runs(entry.Runs, body),
                    ]);
                    p.ParagraphProperties!.Append(
                        new Tabs(new TabStop { Val = TabStopValues.Left, Position = 1000 }),
                        new SpacingBetweenLines { After = DocxUnits.TwipsText(_base * 0.3) },
                        new Indentation { Left = "1000", Hanging = "1000" });
                    Reorder(p.ParagraphProperties!);
                    yield return p;
                }

                break;
            case TranscriptBlock transcript:
                yield return Transcript(transcript, factor, width);
                break;
        }
    }

    private IEnumerable<Paragraph> ListItems(IReadOnlyList<ListItem> items, int numId, int level, RunFormat format)
    {
        foreach (var item in items)
        {
            var paragraph = StyledParagraph(DocxStyleIds.ListParagraph, Runs(item.Runs, format));
            paragraph.ParagraphProperties!.Append(new NumberingProperties(new NumberingLevelReference { Val = Math.Min(level, 2) }, new NumberingId { Val = numId }));
            Reorder(paragraph.ParagraphProperties!);
            yield return paragraph;
            foreach (var nested in ListItems(item.Items, numId, level + 1, format))
            {
                yield return nested;
            }
        }
    }

    private W.Table Table(TableBlock block, double factor, int width)
    {
        var columnCount = Math.Max(block.Columns.Count, block.Rows.Count == 0 ? 1 : block.Rows.Max(r => r.Cells.Count));
        var weights = block.Widths.Count == columnCount && block.Widths.All(w => w > 0) ? block.Widths.ToList() : Enumerable.Repeat(1.0, columnCount).ToList();
        var total = weights.Sum();
        var widths = weights.Select(w => (int)Math.Floor(width * w / total)).ToList();
        var hairline = DocxUnits.Hex(StyleMetrics.Hairline);
        var table = new W.Table(new TableProperties(
            new TableWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = hairline },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = hairline },
                new InsideVerticalBorder { Val = BorderValues.None }),
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = DocxUnits.TwipsText(_base * 0.35), Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = (short)DocxUnits.Twips(_base * 0.6), Type = TableWidthValues.Dxa },
                new BottomMargin { Width = DocxUnits.TwipsText(_base * 0.35), Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = (short)DocxUnits.Twips(_base * 0.6), Type = TableWidthValues.Dxa }),
            new TableLook { Val = "0000", FirstRow = true, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true }));
        table.Append(new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));
        var size = Size(factor * 0.92);
        if (block.Columns.Count > 0)
        {
            var header = new W.TableRow(new TableRowProperties(new TableHeader()));
            for (var c = 0; c < columnCount; c++)
            {
                var text = c < block.Columns.Count ? block.Columns[c] : string.Empty;
                var format = new RunFormat { Bold = true, Size = size, Color = _style.TableHeaderFill ? _head : null };
                header.Append(Cell(widths[c], [TextRun(text, format)], _style.TableHeaderFill ? DocxUnits.Hex(_style.TintHex()) : null));
            }

            table.Append(header);
        }

        foreach (var row in block.Rows)
        {
            var tableRow = new W.TableRow(new TableRowProperties(new CantSplit()));
            for (var c = 0; c < columnCount; c++)
            {
                var runs = c < row.Cells.Count ? Runs(row.Cells[c].Runs, new RunFormat { Size = size }) : [];
                tableRow.Append(Cell(widths[c], runs, null));
            }

            table.Append(tableRow);
        }

        return table;
    }

    /// <summary>The Full transcript as a compact three-column table that Word paginates (header row repeats).</summary>
    private W.Table Transcript(TranscriptBlock block, double factor, int width)
    {
        var timeWidth = DocxUnits.InchesToTwips(0.65);
        var speakerWidth = DocxUnits.InchesToTwips(1.35);
        var textWidth = Math.Max(1440, width - timeWidth - speakerWidth);
        var hairline = DocxUnits.Hex(StyleMetrics.Hairline);
        var table = new W.Table(new TableProperties(
            new TableWidth { Width = (timeWidth + speakerWidth + textWidth).ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = hairline },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = hairline },
                new InsideVerticalBorder { Val = BorderValues.None }),
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = DocxUnits.TwipsText(_base * 0.2), Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 0, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = DocxUnits.TwipsText(_base * 0.2), Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = (short)DocxUnits.Twips(_base * 0.6), Type = TableWidthValues.Dxa }),
            new TableLook { Val = "0000", FirstRow = false, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true }));
        table.Append(new TableGrid(
            new GridColumn { Width = timeWidth.ToString(CultureInfo.InvariantCulture) },
            new GridColumn { Width = speakerWidth.ToString(CultureInfo.InvariantCulture) },
            new GridColumn { Width = textWidth.ToString(CultureInfo.InvariantCulture) }));
        var size = Size(factor * 0.92);
        foreach (var item in block.Interleaved())
        {
            if (item is TranscriptChapter chapter)
            {
                var row = new W.TableRow(new TableRowProperties(new CantSplit()));
                var cell = new W.TableCell(
                    new TableCellProperties(
                        new TableCellWidth { Width = (timeWidth + speakerWidth + textWidth).ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
                        new GridSpan { Val = 3 }),
                    MakeParagraph(DocxStyleIds.TableText, [TextRun(chapter.Title, new RunFormat { Bold = true, Color = _head, Font = StyleMetrics.WordFont(_style.HeadingTypeface), Size = size })], keepNext: true));
                row.Append(cell);
                table.Append(row);
            }
            else if (item is TranscriptLine line)
            {
                var row = new W.TableRow(new TableRowProperties(new CantSplit()));
                row.Append(
                    Cell(timeWidth, [TextRun(Timecode.Format(line.T), new RunFormat { Font = StyleMetrics.MonoWordFont, Size = Size(factor * 0.8), Color = DocxUnits.Hex(StyleMetrics.ChipInk) })], null),
                    Cell(speakerWidth, [TextRun(line.Speaker, new RunFormat { Bold = true, Size = size })], null),
                    Cell(textWidth, [TextRun(line.Text, new RunFormat { Size = size })], null));
                table.Append(row);
            }
        }

        return table;
    }

    private static W.TableCell Cell(int width, IEnumerable<OpenXmlElement> runs, string? fill)
    {
        var properties = new TableCellProperties(new TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa });
        if (fill is not null)
        {
            properties.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill });
        }

        return new W.TableCell(properties, MakeParagraph(DocxStyleIds.TableText, runs, keepNext: false));
    }

    /// <summary>Runs of text; a timestamp run becomes a footnote reference whose note reads "18:42 — see the recording at 0:18:42".</summary>
    private List<OpenXmlElement> Runs(IEnumerable<Run> runs, RunFormat format)
    {
        var result = new List<OpenXmlElement>();
        foreach (var run in runs)
        {
            switch (run.Kind)
            {
                case RunKind.Timestamp when run.T is { } t:
                    result.Add(FootnoteReference(t, run.Text));
                    break;
                case RunKind.Emphasis:
                    result.Add(TextRun(run.Text, format with { Bold = run.IsBold || format.Bold, Italic = run.IsItalic || format.Italic }));
                    break;
                case RunKind.Note:
                    result.Add(TextRun(run.Text, format with { Color = DocxUnits.Hex(StyleMetrics.MetaInk) }));
                    break;
                default:
                    result.Add(TextRun(run.Text, format));
                    break;
            }
        }

        return result;
    }

    private W.Run FootnoteReference(double t, string display)
    {
        var id = _footnotes.Count + 1;
        _footnotes.Add(new W.Footnote(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = DocxStyleIds.FootnoteText }),
            new W.Run(new RunProperties(new RunStyle { Val = DocxStyleIds.FootnoteReference }), new FootnoteReferenceMark()),
            new W.Run(new Text(" " + Timecode.FootnoteText(t, display)) { Space = SpaceProcessingModeValues.Preserve })))
        { Id = id });
        return new W.Run(new RunProperties(new RunStyle { Val = DocxStyleIds.FootnoteReference }), new W.FootnoteReference { Id = id });
    }

    private static W.Run TextRun(string text, RunFormat format)
    {
        var run = new W.Run();
        var properties = format.ToRunProperties();
        if (properties is not null)
        {
            run.Append(properties);
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }

            if (lines[i].Length > 0)
            {
                run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }
        }

        return run;
    }

    private static Paragraph StyledParagraph(string styleId, OpenXmlElement single) => MakeParagraph(styleId, [single], keepNext: false);

    private static Paragraph StyledParagraph(string styleId, IEnumerable<OpenXmlElement> content) => MakeParagraph(styleId, content, keepNext: false);

    private static Paragraph MakeParagraph(string styleId, IEnumerable<OpenXmlElement> content, bool keepNext)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = styleId });
        if (keepNext)
        {
            properties.Append(new KeepNext());
        }

        var paragraph = new Paragraph(properties);
        paragraph.Append(content);
        return paragraph;
    }

    /// <summary>An empty, tiny paragraph: separates consecutive tables (Word would merge them) and ends table cells.</summary>
    private static Paragraph Spacer(double afterPoints) =>
        new(new ParagraphProperties(
                new ParagraphStyleId { Val = DocxStyleIds.TableText },
                new SpacingBetweenLines { Before = "0", After = DocxUnits.TwipsText(afterPoints), Line = "20", LineRule = LineSpacingRuleValues.Exact },
                new ParagraphMarkRunProperties(new FontSize { Val = "2" }, new FontSizeComplexScript { Val = "2" })));

    private double? Size(double factor) => Math.Abs(factor - 1) < 0.001 ? null : _base * factor;

    /// <summary>Puts paragraph property children in schema order after appending.</summary>
    private static void Reorder(ParagraphProperties properties)
    {
        string[] order = ["pStyle", "keepNext", "keepLines", "pageBreakBefore", "widowControl", "numPr", "pBdr", "shd", "tabs", "spacing", "ind", "jc", "outlineLvl", "rPr"];
        var children = properties.ChildElements.ToList();
        properties.RemoveAllChildren();
        properties.Append(children.OrderBy(c => Array.IndexOf(order, c.LocalName) is var i && i < 0 ? order.Length : i));
    }
}
