using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Memento.Documents.Export;
using Memento.Documents.Export.Docx;
using Memento.Documents.Model;
using Memento.Documents.Styling;
using Memento.Documents.Tests.DocumentModel.Support;
using Xunit.Abstractions;
using Document = Memento.Documents.Model.Document;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class DocxExportTests(ITestOutputHelper output)
{
    private static readonly DocxExporter Exporter = new();

    public static TheoryData<string, string> FixturesAndStyles
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var fixture in new[] { "meeting-minutes", "all-shapes" })
            {
                foreach (var style in BuiltInStyles.Ids)
                {
                    data.Add(fixture, style);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FixturesAndStyles))]
    public void OpenXmlValidatorReportsNoErrors(string fixture, string styleId)
    {
        var bytes = Exporter.Export(Fixture(fixture), BuiltInStyles.Get(styleId));
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();
        foreach (var error in errors.Take(20))
        {
            output.WriteLine($"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}");
        }

        Assert.Empty(errors);
    }

    [Fact]
    public void MeetingMinutesHasTheExpectedStructure()
    {
        var source = SampleDocuments.MeetingMinutes();
        var bytes = Exporter.Export(source, BuiltInStyles.Corporate);
        Snapshot.Attach("meeting-minutes.corporate.docx", bytes);
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        // Title, meta line and one module heading per module, in reading order.
        var paragraphs = body.Descendants<Paragraph>().ToList();
        Assert.Equal("Design review: library screen", Text(paragraphs.First(p => StyleOf(p) == DocxStyleIds.Title)));
        Assert.StartsWith("Meeting minutes · Monday 5 October 2026", Text(paragraphs.First(p => StyleOf(p) == DocxStyleIds.Subtitle)), StringComparison.Ordinal);
        var headings = paragraphs.Where(p => StyleOf(p) == DocxStyleIds.Heading1).Select(Text).ToList();
        Assert.Equal(source.Modules().Select(m => m.Title), headings);

        // Side-by-side rows are borderless two-column tables at the top level of the body.
        var layout = body.Elements<Table>().Where(t => t.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count() == 2).ToList();
        Assert.Equal(3, layout.Count);
        Assert.All(layout, t =>
        {
            Assert.Equal(2, t.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count());
            Assert.Equal(BorderValues.None, t.GetFirstChild<TableProperties>()!.TableBorders!.LeftBorder!.Val!.Value);
            Assert.Equal(BorderValues.None, t.GetFirstChild<TableProperties>()!.TableBorders!.InsideVerticalBorder!.Val!.Value);
        });
        Assert.Contains(layout[1].Descendants<Paragraph>(), p => Text(p) == "Action items");

        // Action items: a nested table with a repeating, filled header row.
        var actions = layout[1].Descendants<Table>().Single();
        var header = actions.Elements<TableRow>().First();
        Assert.NotNull(header.TableRowProperties!.GetFirstChild<TableHeader>());
        Assert.Equal("D9E1EC", header.Descendants<Shading>().First().Fill!.Value);
        Assert.Equal(["Action", "Owner", "Due"], header.Elements<TableCell>().Select(c => c.InnerText));

        // Every timestamp is a footnote whose text names the moment.
        var timestamps = source.Modules().SelectMany(RendererTests.Runs).Count(r => r.Kind == RunKind.Timestamp);
        var references = body.Descendants<FootnoteReference>().ToList();
        Assert.Equal(timestamps, references.Count);
        var notes = main.FootnotesPart!.Footnotes!.Elements<Footnote>().Where(f => f.Id!.Value > 0).ToList();
        Assert.Equal(timestamps, notes.Count);
        Assert.Equal("18:42 — see the recording at 0:18:42", notes[0].InnerText.Trim());
        Assert.Equal(references.Select(r => r.Id!.Value), notes.Select(n => n.Id!.Value));

        // The print HTML numbers the same footnotes in the same order.
        var print = new Render.DocumentHtmlRenderer().RenderPrintHtml(source, BuiltInStyles.Corporate);
        for (var i = 0; i < notes.Count; i++)
        {
            Assert.Contains($"<li id=\"fn-{i + 1}\" data-t=\"", print, StringComparison.Ordinal);
            Assert.Contains($">{notes[i].InnerText.Trim()}</li>", print, StringComparison.Ordinal);
        }

        // Page size, margins, running header and page numbers in the section.
        var section = body.GetFirstChild<SectionProperties>()!;
        Assert.Same(section, body.LastChild);
        Assert.Equal(12240u, section.GetFirstChild<PageSize>()!.Width!.Value);
        Assert.Equal(15840u, section.GetFirstChild<PageSize>()!.Height!.Value);
        Assert.Equal(1440u, section.GetFirstChild<PageMargin>()!.Left!.Value);
        var headerPart = (HeaderPart)main.GetPartById(section.GetFirstChild<HeaderReference>()!.Id!);
        Assert.Equal("Design review: library screen\t5 October 2026", string.Concat(headerPart.Header!.Descendants().Select(e => e is TabChar ? "\t" : e is Text t ? t.Text : string.Empty)));
        var footerPart = (FooterPart)main.GetPartById(section.GetFirstChild<FooterReference>()!.Id!);
        Assert.Equal([" PAGE ", " NUMPAGES "], footerPart.Footer!.Descendants<SimpleField>().Select(f => f.Instruction!.Value));

        // The Full transcript is a compact three-column table with chapter rows.
        var transcript = body.Elements<Table>().Last();
        Assert.Equal(3, transcript.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count());
        Assert.Equal(72 + 4, transcript.Elements<TableRow>().Count());

        Assert.Equal("Design review: library screen", doc.PackageProperties.Title);
    }

    [Fact]
    public void StylesFollowTheDocumentStyle()
    {
        var corporate = Styles(BuiltInStyles.Corporate);
        var heading = corporate.Elements<Style>().Single(s => s.StyleId == DocxStyleIds.Heading1);
        Assert.Equal("1F3A5F", heading.StyleRunProperties!.Color!.Val!.Value);
        Assert.NotNull(heading.StyleRunProperties.Caps);
        Assert.Equal("Segoe UI", heading.StyleRunProperties.RunFonts!.Ascii!.Value);
        Assert.Null(heading.StyleParagraphProperties!.NumberingProperties);
        var title = corporate.Elements<Style>().Single(s => s.StyleId == DocxStyleIds.Title);
        Assert.Equal("1F3A5F", title.StyleParagraphProperties!.ParagraphBorders!.BottomBorder!.Color!.Value);
        Assert.Equal("22", corporate.DocDefaults!.Descendants<FontSize>().Single().Val!.Value);

        var academic = Styles(BuiltInStyles.Academic);
        var academicHeading = academic.Elements<Style>().Single(s => s.StyleId == DocxStyleIds.Heading1);
        Assert.NotNull(academicHeading.StyleParagraphProperties!.NumberingProperties);
        Assert.Null(academicHeading.StyleRunProperties!.Caps);
        Assert.Equal("Georgia", academicHeading.StyleRunProperties.RunFonts!.Ascii!.Value);
        Assert.Null(academic.Elements<Style>().Single(s => s.StyleId == DocxStyleIds.Title).StyleParagraphProperties!.ParagraphBorders);

        var large = Styles(BuiltInStyles.Minimal with { BaseSize = BaseSize.Large, HeadingColor = HeadingColor.Burgundy });
        Assert.Equal("25", large.DocDefaults!.Descendants<FontSize>().Single().Val!.Value);
        Assert.Equal("7A2E2E", large.Elements<Style>().Single(s => s.StyleId == DocxStyleIds.Heading1).StyleRunProperties!.Color!.Val!.Value);
    }

    [Fact]
    public void MinimalDrawsLinesBetweenSectionsAndNoHeaderFill()
    {
        using var doc = Open(SampleDocuments.MeetingMinutes(), BuiltInStyles.Minimal);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var ruledHeadings = body.Elements<Paragraph>().Where(p => p.ParagraphProperties?.ParagraphBorders?.TopBorder is not null).ToList();
        Assert.NotEmpty(ruledHeadings);
        Assert.All(body.Elements<Table>().Where(t => t.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count() == 2), t => Assert.Equal(BorderValues.Single, t.GetFirstChild<TableProperties>()!.TableBorders!.TopBorder!.Val!.Value));
        Assert.DoesNotContain(body.Descendants<Shading>(), s => s.Fill?.Value == "E8E6E0");
        var section = body.GetFirstChild<SectionProperties>()!;
        Assert.Null(section.GetFirstChild<HeaderReference>());
        Assert.NotNull(section.GetFirstChild<FooterReference>());
    }

    [Fact]
    public void A4PaperAndNoPageParts()
    {
        using var doc = Open(SampleDocuments.AllShapes(), BuiltInStyles.Corporate with { Paper = PaperSize.A4, PageNumbers = false, RunningHeader = false });
        var section = doc.MainDocumentPart!.Document!.Body!.GetFirstChild<SectionProperties>()!;
        Assert.Equal(11906u, section.GetFirstChild<PageSize>()!.Width!.Value);
        Assert.Equal(16838u, section.GetFirstChild<PageSize>()!.Height!.Value);
        Assert.Null(section.GetFirstChild<HeaderReference>());
        Assert.Null(section.GetFirstChild<FooterReference>());
        Assert.Empty(doc.MainDocumentPart.HeaderParts);
    }

    [Fact]
    public void ListsChipsQuotesTimelinesAndThreeColumns()
    {
        using var doc = Open(SampleDocuments.AllShapes(), BuiltInStyles.Corporate);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;
        var listParagraphs = body.Descendants<Paragraph>().Where(p => StyleOf(p) == DocxStyleIds.ListParagraph).ToList();
        Assert.Equal(10, listParagraphs.Count);
        Assert.Contains(listParagraphs, p => p.ParagraphProperties!.NumberingProperties!.NumberingLevelReference!.Val!.Value == 2);
        var numbering = main.NumberingDefinitionsPart!.Numbering!;
        Assert.Contains(numbering.Elements<NumberingInstance>(), n => n.Elements<LevelOverride>().Any());

        Assert.Equal(3, body.Elements<Table>().Single(t => t.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count() == 3 && t.Descendants<Table>().Any()).GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count());
        Assert.Contains(body.Descendants<Shading>(), s => s.Fill?.Value == "EDEBE6");
        Assert.Contains(body.Descendants<Paragraph>(), p => StyleOf(p) == DocxStyleIds.Quote && Text(p) == "— Alex Moreau · 8:32");
        Assert.Contains(body.Descendants<Paragraph>(), p => Text(p) == "1:02:05\tClose");
        Assert.Contains(body.Descendants<Paragraph>(), p => StyleOf(p) == DocxStyleIds.Heading2 && Text(p) == "A sub-heading");
        Assert.Contains(body.Descendants<Paragraph>(), p => StyleOf(p) == DocxStyleIds.Heading3 && Text(p) == "A smaller sub-heading");
        Assert.Contains(body.Descendants<Break>(), b => b.Parent is DocumentFormat.OpenXml.Wordprocessing.Run);
    }

    [Fact]
    public void DocumentXmlSnapshot()
    {
        using var doc = Open(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate);
        var xml = XDocument.Parse(doc.MainDocumentPart!.Document!.OuterXml).ToString();
        Snapshot.Match("meeting-minutes.corporate.document.xml", xml + "\n");
    }

    internal static Document Fixture(string name) => name == "meeting-minutes" ? SampleDocuments.MeetingMinutes() : SampleDocuments.AllShapes();

    internal static WordprocessingDocument Open(Document document, DocumentStyle style) =>
        WordprocessingDocument.Open(new MemoryStream(Exporter.Export(document, style)), false);

    private static Styles Styles(DocumentStyle style)
    {
        using var doc = Open(SampleDocuments.AllShapes(), style);
        return (Styles)doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.CloneNode(true);
    }

    private static string? StyleOf(Paragraph paragraph) => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

    private static string Text(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants().Select(e => e switch { Text t => t.Text, TabChar => "\t", _ => string.Empty }));
}
