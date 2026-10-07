using System.Globalization;
using Memento.Documents.Agenda.Layout;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.Pdf;

/// <summary>
/// A PDF with no text layer (a scanned agenda): each page rendered at <see cref="Dpi"/> dpi and read with text
/// recognition like a photo, then the pages structured together. The result says that it was recognized, so the
/// items are checked against the original.
/// </summary>
internal static class ScannedPdfReader
{
    /// <summary>200 dpi puts 11 pt body text at about 30 px, above the 24 px the recognizer wants.</summary>
    public const int Dpi = 200;

    /// <summary>Agendas are short; a long scanned document is read for its first pages only.</summary>
    public const int MaxPages = 10;

    public static async Task<AgendaParseResult> ReadAsync(
        ReadOnlyMemory<byte> pdf,
        AgendaParseOptions options,
        IReadOnlyList<IOcrEngine> engines,
        IPdfPageRenderer renderer,
        CancellationToken cancellationToken)
    {
        IOcrEngine engine;
        try
        {
            engine = await ImageAgendaParser.ChooseEngineAsync(engines, options, cancellationToken).ConfigureAwait(false);
        }
        catch (AgendaImportException e) when (e.Code == AgendaErrorCodes.OcrUnavailable)
        {
            throw AgendaErrors.NoText(
                options,
                "has no text to read; it may be a scan or a photo saved as a PDF",
                "Text recognition, which reads scans, is not set up on this PC: add a language with \"Optical character recognition\" in Windows Settings › Time & language › Language & region and import it again, or paste the items as text.");
        }

        var images = await renderer.RenderAsync(pdf, Dpi, MaxPages, options, cancellationToken).ConfigureAwait(false);
        var lines = new List<SourceLine>();
        var warnings = new List<AgendaParseWarning>();
        var words = 0;
        for (var index = 0; index < images.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = index + 1;
            var page = await engine.RecognizeAsync(images[index], new OcrRequest(options.OcrLanguage, MaxImageSide: Math.Max(options.MaxImageSide, 20_000)), cancellationToken).ConfigureAwait(false);
            if (page.Words.Count == 0)
            {
                continue;
            }

            words += page.Words.Count;
            var boxes = page.Words.Select(w => new WordBox(w.Text, w.Left, w.Top, w.Width, w.Height, Confidence: w.Confidence)).ToList();
            var (ordered, twoColumns) = PageLayout.ReadingOrder(boxes);
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

            lines.AddRange(LayoutLines.ToSourceLines(ordered, number, fromOcr: true, ImageAgendaParser.ReasonsFor));
        }

        if (words == 0)
        {
            throw AgendaErrors.NoText(
                options,
                "has no text layer, and text recognition found no words on its pages; it may be a blank or very faint scan",
                "Scan the agenda again with more contrast, take a photo of it, or paste the items as text.");
        }

        warnings.Insert(
            0,
            new AgendaParseWarning(
                AgendaWarningCodes.ScannedPdf,
                engine.ReportsConfidence
                    ? "This PDF has no text layer, so its pages were read with text recognition. Words it was unsure about are marked; check them against the original."
                    : "This PDF has no text layer, so its pages were read with text recognition, which can misread letters and numbers. Check the items against the original."));
        if (images.Count == MaxPages)
        {
            warnings.Add(new AgendaParseWarning(
                AgendaWarningCodes.Unparsed,
                string.Create(CultureInfo.InvariantCulture, $"Only the first {MaxPages} pages of the scan were read. Import the remaining pages on their own if items are missing.")));
        }

        var structured = AgendaStructurer.Structure(lines, cancellationToken);
        return AgendaResults.Create(AgendaSourceKind.Pdf, options, structured, warnings, engine.Id);
    }
}
