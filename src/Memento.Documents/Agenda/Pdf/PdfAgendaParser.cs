using System.Globalization;
using Memento.Documents.Agenda.Layout;
using Memento.Documents.Agenda.Text;
using Memento.Documents.Agenda.Ocr;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace Memento.Documents.Agenda.Pdf;

/// <summary>
/// PDF files with a text layer: words with their positions, lines rebuilt from word boxes, two-column pages read
/// left column first (with a warning), headings from larger or bold type, numbering from the text. A PDF with no text
/// layer (a scan) is rendered page by page and read with text recognition when an OCR engine and a renderer are given.
/// </summary>
public sealed class PdfAgendaParser : IAgendaParser
{
    private const int MaxPages = 200;

    private readonly List<IOcrEngine> _engines;
    private readonly IPdfPageRenderer? _renderer;

    /// <summary>Text-layer PDFs only: a scan is refused with <c>agenda.noText</c>.</summary>
    public PdfAgendaParser()
        : this([], null)
    {
    }

    /// <summary>Scans are rendered with <paramref name="renderer"/> and read with the first available engine.</summary>
    public PdfAgendaParser(IEnumerable<IOcrEngine> engines, IPdfPageRenderer? renderer)
    {
        ArgumentNullException.ThrowIfNull(engines);
        _engines = engines.ToList();
        _renderer = renderer;
    }

    public IReadOnlyList<AgendaSourceKind> Kinds { get; } = [AgendaSourceKind.Pdf];

    public bool CanParse(string fileName, string? contentType) =>
        AgendaResults.HasExtension(fileName, ".pdf") || AgendaResults.HasContentType(contentType, "application/pdf");

    public async Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await AgendaContent.ReadAsync(content, options, cancellationToken).ConfigureAwait(false);
        var canRecognize = _renderer is not null && _engines.Count > 0;
        var result = await Task.Run(() => Parse(bytes, options, canRecognize, cancellationToken), cancellationToken).ConfigureAwait(false);
        return result ?? await ScannedPdfReader.ReadAsync(bytes, options, _engines, _renderer!, cancellationToken).ConfigureAwait(false);
    }

    /// <returns><c>null</c> when the PDF has no text layer and <paramref name="canRecognize"/> is set.</returns>
    private static AgendaParseResult? Parse(ReadOnlyMemory<byte> bytes, AgendaParseOptions options, bool canRecognize, CancellationToken cancellationToken)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes.ToArray(), new ParsingOptions { UseLenientParsing = true, ClipPaths = false, SkipMissingFonts = true });
        }
        catch (PdfDocumentEncryptedException e)
        {
            throw AgendaErrors.Protected(options, e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw AgendaErrors.Unreadable(options, "a PDF", e);
        }

        using (document)
        {
            var lines = new List<SourceLine>();
            var warnings = new List<AgendaParseWarning>();
            var rotated = new List<string>();
            var wordCount = 0;
            var pageCount = Math.Min(document.NumberOfPages, MaxPages);
            for (var number = 1; number <= pageCount; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Page page;
                List<Word> pageWords;
                try
                {
                    page = document.GetPage(number);
                    pageWords = page.GetWords().ToList();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw AgendaErrors.Unreadable(options, "a PDF", e);
                }

                var words = new List<WordBox>(pageWords.Count);
                foreach (var word in pageWords)
                {
                    if (string.IsNullOrWhiteSpace(word.Text))
                    {
                        continue;
                    }

                    if (word.TextOrientation != TextOrientation.Horizontal)
                    {
                        rotated.Add(word.Text);
                        continue;
                    }

                    words.Add(ToBox(word, page.Height));
                }

                wordCount += words.Count;
                var (ordered, twoColumns) = PageLayout.ReadingOrder(words);
                if (twoColumns)
                {
                    warnings.Add(new AgendaParseWarning(
                        AgendaWarningCodes.MultiColumn,
                        string.Create(CultureInfo.InvariantCulture, $"Page {number} seems to have two columns. The left column was read first, then the right one; reorder the items here if the agenda runs across."),
                        Location: new AgendaSourceLocation { Page = number }));
                }

                if (lines.Count > 0)
                {
                    lines.Add(SourceLine.Blank(new AgendaSourceLocation { Page = number }));
                }

                lines.AddRange(LayoutLines.ToSourceLines(ordered, number, fromOcr: false));
            }

            if (wordCount == 0)
            {
                if (canRecognize)
                {
                    return null;
                }

                throw AgendaErrors.NoText(
                    options,
                    "has no text to read; it may be a scan or a photo saved as a PDF",
                    "Save the agenda page as a picture (PNG or JPEG) and import that, so text recognition can read it, or paste the items as text.");
            }

            if (rotated.Count > 0)
            {
                warnings.Add(new AgendaParseWarning(
                    AgendaWarningCodes.Unparsed,
                    "Some rotated or vertical text was not read as agenda items. Add anything from it that belongs in the agenda here.",
                    string.Join(' ', rotated)));
            }

            if (document.NumberOfPages > MaxPages)
            {
                warnings.Add(new AgendaParseWarning(
                    AgendaWarningCodes.Unparsed,
                    string.Create(CultureInfo.InvariantCulture, $"Only the first {MaxPages} of {document.NumberOfPages} pages were read. Save the agenda pages on their own and import them if items are missing.")));
            }

            var structured = AgendaStructurer.Structure(lines, cancellationToken);
            return AgendaResults.Create(AgendaSourceKind.Pdf, options, structured, warnings);
        }
    }

    private static WordBox ToBox(Word word, double pageHeight)
    {
        var box = word.BoundingBox;
        var sizes = word.Letters.Select(l => l.PointSize).Where(s => s > 0).ToList();
        var bold = word.Letters.Count > 0 && word.Letters.All(l =>
            l.FontDetails?.IsBold == true ||
            (l.FontName ?? string.Empty).Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
            (l.FontName ?? string.Empty).Contains("Black", StringComparison.OrdinalIgnoreCase) ||
            (l.FontName ?? string.Empty).Contains("Heavy", StringComparison.OrdinalIgnoreCase));
        var height = Math.Max(box.Height, sizes.Count > 0 ? sizes.Average() * 0.7 : 1);
        return new WordBox(
            word.Text,
            box.Left,
            pageHeight - box.Top,
            Math.Max(box.Width, 0.1),
            height,
            sizes.Count > 0 ? Math.Round(LineBox.Median(sizes), 1) : null,
            bold);
    }
}
