using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using S = DocumentFormat.OpenXml.Spreadsheet;
using V = DocumentFormat.OpenXml.Vml;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Memento.Documents.Tests.Support;

/// <summary>
/// Writes the binary agenda fixtures (Word, Excel, PDF and rendered images). Everything in them is fictional. The
/// committed files are what the tests read; run <see cref="FixtureGeneratorTests"/> with
/// <c>MEMENTO_REGENERATE_FIXTURES=1</c> to rebuild them after changing this file.
/// </summary>
internal static class FixtureGenerator
{
    public static void WriteAll(string directory)
    {
        Directory.CreateDirectory(directory);
        WriteMessyDocx(Path.Combine(directory, "messy-mixed.docx"));
        WriteBoardTableDocx(Path.Combine(directory, "board-table.docx"));
        WritePlanningXlsx(Path.Combine(directory, "planning-second-sheet.xlsx"));
        WriteSimpleXlsx(Path.Combine(directory, "simple-list.xlsx"));
        WriteTwoColumnPdf(Path.Combine(directory, "two-column.pdf"));
        WriteNumberedPdf(Path.Combine(directory, "numbered-merge.pdf"));
        WriteCleanPng(Path.Combine(directory, "ocr-clean.png"));
        WritePhoto(Path.Combine(directory, "ocr-photo.png"), ImageFormat.Png);
        WritePhoto(Path.Combine(directory, "ocr-photo.jpg"), ImageFormat.Jpeg);
        WriteSmallPng(Path.Combine(directory, "ocr-small.png"));
    }

    // ---------------------------------------------------------------- Word

    private static void WriteMessyDocx(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        AddWordStyles(main);
        AddWordNumbering(main);
        var body = new W.Body(
            Paragraph("Neighbourhood Garden Association", style: "Title"),
            Paragraph("Date: 21 September 2031"),
            Paragraph("Agenda", style: "Heading1"),
            Paragraph("Welcome from the chair", numId: 1, level: 0),
            Paragraph("Apologies for absence", numId: 1, level: 0),
            Paragraph("Minutes of the 2030 AGM", numId: 1, level: 0),
            Paragraph("4. Treasurer's report"),
            Paragraph("Election of committee members", numId: 1, level: 0),
            Paragraph("Chair", numId: 1, level: 1),
            Paragraph("Treasurer", numId: 1, level: 1),
            TextBoxParagraph("Bring your membership card"),
            Paragraph("Members' proposals", style: "Heading2"),
            Paragraph("Raised beds for the school plot", style: "ListBullet"),
            Paragraph("Rainwater tanks", style: "ListBullet"),
            Paragraph("• Tool shed repairs"),
            new W.Paragraph(
                new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading2" }),
                new W.Run(new W.Text("Any other business"), new W.Break(), new W.Text("Date of next meeting"))),
            Table(
                ["Committee role", "Current holder"],
                ["Chair", "P. Novak"],
                ["Treasurer", "L. Haddad"]));
        main.Document = new W.Document(body);
        main.Document.Save();
    }

    private static void WriteBoardTableDocx(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        AddWordStyles(main);
        var table = Table(
            ["No.", "Item", "Presenter", "Time"],
            ["1", "Opening and quorum", "Chair", "18:00"],
            ["2", "Conflicts of interest", "Chair", "18:05"],
            ["3", "Chief executive's report", "A. Moreau", "18:10"],
            ["4", "Quarterly accounts", "Treasurer", "18:40"]);
        var merged = new W.TableRow(new W.TableCell(
            new W.TableCellProperties(new W.GridSpan { Val = 4 }),
            new W.Paragraph(new W.Run(new W.Text("Closed session")))));
        table.Append(merged);
        table.Append(Row(["5", "Remuneration review", "Chair", "19:10"]));
        table.Append(Row(["6", "Succession planning", "Chair", "19:30"]));
        main.Document = new W.Document(new W.Body(
            Paragraph("Board meeting agenda", style: "Title"),
            Paragraph("Location: Room 2, Harbour House"),
            table));
        main.Document.Save();
    }

    private static void AddWordStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new W.Styles(
            Style("Normal", "Normal", null),
            Style("Title", "Title", "Normal"),
            Style("Heading1", "heading 1", "Normal", outline: 0),
            Style("Heading2", "heading 2", "Normal", outline: 1),
            new W.Style(
                new W.StyleName { Val = "List Bullet" },
                new W.BasedOn { Val = "Normal" },
                new W.StyleParagraphProperties(new W.NumberingProperties(new W.NumberingLevelReference { Val = 0 }, new W.NumberingId { Val = 2 })))
            {
                Type = W.StyleValues.Paragraph,
                StyleId = "ListBullet",
            });
        part.Styles.Save();
    }

    private static W.Style Style(string id, string name, string? basedOn, int? outline = null)
    {
        var style = new W.Style(new W.StyleName { Val = name }) { Type = W.StyleValues.Paragraph, StyleId = id };
        if (basedOn is not null)
        {
            style.Append(new W.BasedOn { Val = basedOn });
        }

        if (outline is { } level)
        {
            style.Append(new W.StyleParagraphProperties(new W.OutlineLevel { Val = level }));
        }

        return style;
    }

    private static void AddWordNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering = new W.Numbering(
            new W.AbstractNum(
                Level(0, W.NumberFormatValues.Decimal, "%1."),
                Level(1, W.NumberFormatValues.LowerLetter, "%2)"))
            { AbstractNumberId = 0 },
            new W.AbstractNum(Level(0, W.NumberFormatValues.Bullet, "•")) { AbstractNumberId = 1 },
            new W.NumberingInstance(new W.AbstractNumId { Val = 0 }) { NumberID = 1 },
            new W.NumberingInstance(new W.AbstractNumId { Val = 1 }) { NumberID = 2 });
        part.Numbering.Save();
    }

    private static W.Level Level(int index, W.NumberFormatValues format, string text) =>
        new(new W.StartNumberingValue { Val = 1 }, new W.NumberingFormat { Val = format }, new W.LevelText { Val = text })
        {
            LevelIndex = index,
        };

    private static W.Paragraph Paragraph(string text, string? style = null, int? numId = null, int level = 0)
    {
        var properties = new W.ParagraphProperties();
        if (style is not null)
        {
            properties.Append(new W.ParagraphStyleId { Val = style });
        }

        if (numId is { } id)
        {
            properties.Append(new W.NumberingProperties(new W.NumberingLevelReference { Val = level }, new W.NumberingId { Val = id }));
        }

        return new W.Paragraph(properties, new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static W.Paragraph TextBoxParagraph(string text) =>
        new(new W.Run(new W.Picture(new V.Shape(
            new V.TextBox(new W.TextBoxContent(new W.Paragraph(new W.Run(new W.Text(text))))))
        {
            Id = "TextBox1",
            Style = "width:200pt;height:40pt",
        })));

    private static W.Table Table(params string[][] rows)
    {
        var columns = rows.Max(r => r.Length);
        var table = new W.Table(
            new W.TableProperties(new W.TableBorders()),
            new W.TableGrid(Enumerable.Range(0, columns).Select(_ => new W.GridColumn { Width = "2400" })));
        foreach (var row in rows)
        {
            table.Append(Row(row));
        }

        return table;
    }

    private static W.TableRow Row(string[] cells) =>
        new(cells.Select(c => new W.TableCell(new W.Paragraph(new W.Run(new W.Text(c))))));

    // ---------------------------------------------------------------- Excel

    private static void WritePlanningXlsx(string path)
    {
        var strings = new List<string>();
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new S.Workbook(new S.Sheets());
        AddExcelStyles(workbook);

        AddSheet(workbook, "Notes", strings, [], ("A1", "Prepared by the planning team"), ("A2", "Version 3"));
        AddSheet(
            workbook,
            "Agenda",
            strings,
            ["A1:C1", "A8:C8"],
            ("A1", "Planning day at Northwind Studio"),
            ("A3", "Time"),
            ("B3", "Topic"),
            ("C3", "Lead"),
            ("A4", 9.0 / 24),
            ("B4", "Arrival and coffee"),
            ("C4", "Front desk"),
            ("A5", 9.5 / 24),
            ("B5", "Where we are now"),
            ("C5", "K. Ito"),
            ("A6", 10.25 / 24),
            ("B6", "Goals for the next quarter"),
            ("C6", "K. Ito"),
            ("A7", 12.0 / 24),
            ("B7", "Lunch"),
            ("A8", "Afternoon"),
            ("A9", 13.5 / 24),
            ("B9", "Team breakouts"),
            ("C9", "Leads"),
            ("A10", 16.0 / 24),
            ("B10", "Share-back and decisions"),
            ("C10", new InlineText("K. Ito")));

        var shared = workbook.AddNewPart<SharedStringTablePart>();
        shared.SharedStringTable = new S.SharedStringTable(strings.Select(s => new S.SharedStringItem(new S.Text(s))))
        {
            Count = (uint)strings.Count,
            UniqueCount = (uint)strings.Count,
        };
        shared.SharedStringTable.Save();
        workbook.Workbook.Save();
    }

    private static void WriteSimpleXlsx(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new S.Workbook(new S.Sheets());
        AddSheet(
            workbook,
            "Sheet1",
            strings: null,
            [],
            ("A1", new InlineText("Agenda")),
            ("A2", new InlineText("Introductions")),
            ("A3", new InlineText("Quarterly numbers")),
            ("A4", new InlineText("1.1 Revenue")),
            ("A5", new InlineText("1.2 Costs")),
            ("A6", new InlineText("Hiring")),
            ("A7", new InlineText("Close")));
        workbook.Workbook.Save();
    }

    private static void AddExcelStyles(WorkbookPart workbook)
    {
        var part = workbook.AddNewPart<WorkbookStylesPart>();
        part.Stylesheet = new S.Stylesheet(
            new S.Fonts(new S.Font()) { Count = 1 },
            new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None })) { Count = 1 },
            new S.Borders(new S.Border()) { Count = 1 },
            new S.CellFormats(
                new S.CellFormat { NumberFormatId = 0 },
                new S.CellFormat { NumberFormatId = 20, ApplyNumberFormat = true })
            { Count = 2 });
        part.Stylesheet.Save();
    }

    private static void AddSheet(WorkbookPart workbook, string name, List<string>? strings, string[] merges, params (string Reference, object Value)[] cells)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        foreach (var group in cells.GroupBy(c => uint.Parse(new string(c.Reference.Where(char.IsAsciiDigit).ToArray()), System.Globalization.CultureInfo.InvariantCulture)).OrderBy(g => g.Key))
        {
            var row = new S.Row { RowIndex = group.Key };
            foreach (var (reference, value) in group)
            {
                row.Append(value switch
                {
                    double number => new S.Cell { CellReference = reference, CellValue = new S.CellValue(number), StyleIndex = 1 },
                    InlineText inline => new S.Cell(new S.InlineString(new S.Text(inline.Text))) { CellReference = reference, DataType = S.CellValues.InlineString },
                    string text when strings is not null => SharedCell(reference, text, strings),
                    string text => new S.Cell(new S.InlineString(new S.Text(text))) { CellReference = reference, DataType = S.CellValues.InlineString },
                    _ => throw new ArgumentException("Unsupported cell value.", nameof(cells)),
                });
            }

            data.Append(row);
        }

        var worksheet = new S.Worksheet(data);
        if (merges.Length > 0)
        {
            worksheet.Append(new S.MergeCells(merges.Select(m => new S.MergeCell { Reference = m })) { Count = (uint)merges.Length });
        }

        part.Worksheet = worksheet;
        part.Worksheet.Save();
        var sheets = workbook.Workbook!.Sheets!;
        sheets.Append(new S.Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = (uint)(sheets.ChildElements.Count + 1),
            Name = name,
        });
    }

    private static S.Cell SharedCell(string reference, string text, List<string> strings)
    {
        var index = strings.IndexOf(text);
        if (index < 0)
        {
            strings.Add(text);
            index = strings.Count - 1;
        }

        return new S.Cell { CellReference = reference, DataType = S.CellValues.SharedString, CellValue = new S.CellValue(index) };
    }

    private sealed record InlineText(string Text);

    // ---------------------------------------------------------------- PDF

    private static void WriteTwoColumnPdf(string path)
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = builder.AddPage(595, 842);
        page.AddText("Regional volunteers day agenda", 16, new PdfPoint(170, 780), bold);
        page.AddText("Morning", 13, new PdfPoint(60, 730), bold);
        page.AddText("Afternoon", 13, new PdfPoint(320, 730), bold);
        string[] left = ["09:00 Registration and name badges", "09:30 Welcome talk from the hosts", "10:00 Project fair in the main hall", "11:30 Lightning talks by local groups"];
        string[] right = ["13:00 Lunch on the terrace together", "14:00 Workshops in the garden rooms", "15:30 Awards for long service", "16:00 Close and thank you"];
        for (var i = 0; i < left.Length; i++)
        {
            page.AddText(left[i], 11, new PdfPoint(60, 705 - (i * 20)), regular);
            page.AddText(right[i], 11, new PdfPoint(320, 705 - (i * 20)), regular);
        }

        File.WriteAllBytes(path, builder.Build());
    }

    private static void WriteNumberedPdf(string path)
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = builder.AddPage(595, 842);
        page.AddText("Quarterly review agenda", 16, new PdfPoint(72, 780), bold);
        page.AddText("Date: 2 October 2031", 11, new PdfPoint(72, 750), regular);
        page.AddText("1. Opening remarks", 11, new PdfPoint(72, 720), regular);
        page.AddText("2. Sales update", 11, new PdfPoint(72, 700), regular);
        page.AddText("3. Customer feedback from the spring survey, including the", 11, new PdfPoint(72, 680), regular);
        page.AddText("themes raised by the regional teams", 11, new PdfPoint(86, 666), regular);
        page.AddText("4. Product roadmap", 11, new PdfPoint(72, 646), regular);
        var heading = page.AddText("5. Decisions", 11, new PdfPoint(72, 626), bold);
        var after = heading[^1].EndBaseLine.X + 4;
        page.AddText("Approve the hiring plan", 11, new PdfPoint(after, 626), regular);
        page.AddText("6. Close", 11, new PdfPoint(72, 606), regular);
        page.AddText("Page 1 of 1", 9, new PdfPoint(270, 40), regular);
        File.WriteAllBytes(path, builder.Build());
    }

    // ---------------------------------------------------------------- Images

    private static void WriteCleanPng(string path)
    {
        using var bitmap = new Bitmap(1400, 640);
        using (var g = Graphics.FromImage(bitmap))
        {
            Prepare(g, Color.White);
            using var title = new Font("Arial", 40, FontStyle.Bold, GraphicsUnit.Pixel);
            using var body = new Font("Arial", 28, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString("Design review agenda", title, Brushes.Black, 80, 50);
            string[] lines = ["1. Welcome and goals", "2. Walkthrough of the new library screen", "3. Feedback round", "4. Decisions and next steps", "5. Close"];
            for (var i = 0; i < lines.Length; i++)
            {
                g.DrawString(lines[i], body, Brushes.Black, 80, 150 + (i * 56));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void WritePhoto(string path, ImageFormat format)
    {
        // A phone photo of a printed page: warm paper, grey ink, rotated 3.5°, sensor noise and a little blur.
        using var page = new Bitmap(1000, 640);
        using (var g = Graphics.FromImage(page))
        {
            Prepare(g, Color.FromArgb(236, 230, 216));
            g.TranslateTransform(500, 320);
            g.RotateTransform(3.5f);
            g.TranslateTransform(-500, -320);
            using var ink = new SolidBrush(Color.FromArgb(48, 46, 52));
            using var title = new Font("Arial", 40, FontStyle.Bold, GraphicsUnit.Pixel);
            using var body = new Font("Arial", 30, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString("Garden committee agenda", title, ink, 100, 100);
            string[] lines = ["- Treasurer update", "- Seed swap in April", "- Volunteer rota for the summer", "- Any other business"];
            for (var i = 0; i < lines.Length; i++)
            {
                g.DrawString(lines[i], body, ink, 100, 200 + (i * 64));
            }
        }

        using var noisy = AddNoiseAndBlur(page, seed: 42);
        if (format.Equals(ImageFormat.Jpeg))
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
            noisy.Save(path, codec, parameters);
        }
        else
        {
            noisy.Save(path, format);
        }
    }

    private static void WriteSmallPng(string path)
    {
        using var bitmap = new Bitmap(420, 150);
        using (var g = Graphics.FromImage(bitmap))
        {
            Prepare(g, Color.White);
            using var title = new Font("Arial", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            using var body = new Font("Arial", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString("Team lunch agenda", title, Brushes.Black, 20, 16);
            string[] lines = ["1. Menu choices", "2. Budget per person", "3. Date and time"];
            for (var i = 0; i < lines.Length; i++)
            {
                g.DrawString(lines[i], body, Brushes.Black, 20, 48 + (i * 24));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void Prepare(Graphics g, Color background)
    {
        g.Clear(background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    private static Bitmap AddNoiseAndBlur(Bitmap source, int seed)
    {
        var random = new Random(seed);
        var width = source.Width;
        var height = source.Height;
        var pixels = new int[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = source.GetPixel(x, y);
                pixels[x, y] = (c.R + c.G + c.B) / 3;
            }
        }

        // Grain in 2 × 2 blocks keeps the committed PNG small; a left-to-right shadow imitates uneven light.
        var grain = new int[(width / 2) + 1, (height / 2) + 1];
        for (var y = 0; y < grain.GetLength(1); y++)
        {
            for (var x = 0; x < grain.GetLength(0); x++)
            {
                grain[x, y] = random.Next(-14, 15);
            }
        }

        var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                var count = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var sx = Math.Clamp(x + dx, 0, width - 1);
                        var sy = Math.Clamp(y + dy, 0, height - 1);
                        sum += pixels[sx, sy];
                        count++;
                    }
                }

                var shade = Math.Clamp((sum / count) + grain[x / 2, y / 2] - (int)(18.0 * x / width), 0, 255);
                result.SetPixel(x, y, Color.FromArgb(shade, shade, shade));
            }
        }

        return result;
    }
}
