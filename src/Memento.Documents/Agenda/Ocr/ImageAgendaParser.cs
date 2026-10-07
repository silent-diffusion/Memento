using System.Globalization;
using Memento.Documents.Agenda.Layout;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// Photos and scans of a printed agenda (PNG, JPEG, BMP, TIFF, HEIC when Windows can decode it): text recognition,
/// lines rebuilt from word boxes, then the same rules as plain text. Words that look misread mark their item.
/// </summary>
public sealed class ImageAgendaParser : IAgendaParser
{
    /// <summary>Below this Tesseract confidence a word marks its item uncertain.</summary>
    private const double LowConfidence = 0.6;

    private readonly List<IOcrEngine> _engines;

    public ImageAgendaParser(IEnumerable<IOcrEngine> engines)
    {
        _engines = engines.ToList();
    }

    public IReadOnlyList<AgendaSourceKind> Kinds { get; } = [AgendaSourceKind.Image];

    public bool CanParse(string fileName, string? contentType) =>
        AgendaResults.HasExtension(fileName, ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".tif", ".tiff", ".heic", ".heif") ||
        AgendaResults.HasContentType(contentType, "image/png", "image/jpeg", "image/bmp", "image/tiff", "image/heic", "image/heif");

    public async Task<AgendaParseResult> ParseAsync(Stream content, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bytes = await AgendaContent.ReadAsync(content, options, cancellationToken).ConfigureAwait(false);
        return await ParseGuard.RunAsync(options, "an image", token => ParseAsync(bytes, options, token), cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgendaParseResult> ParseAsync(ReadOnlyMemory<byte> bytes, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        var format = ImageFormats.Detect(bytes.Span);
        if (format is null or ImageFormats.Gif or ImageFormats.Webp)
        {
            throw AgendaErrors.Unsupported(options, format is null ? "not an image Memento recognizes" : $"a {format} image", "Save it as PNG or JPEG and import it again.");
        }

        if (ImageFormats.TryGetSize(bytes.Span, out var width, out var height) && (width > options.MaxImageSide || height > options.MaxImageSide))
        {
            throw AgendaErrors.ImageTooLarge(options, width, height);
        }

        var engine = await ChooseEngineAsync(_engines, options, cancellationToken).ConfigureAwait(false);
        var page = await engine.RecognizeAsync(bytes, new OcrRequest(options.OcrLanguage, MaxImageSide: options.MaxImageSide), cancellationToken).ConfigureAwait(false);
        if (page.Words.Count == 0)
        {
            throw AgendaErrors.NoText(
                options,
                "has no text that text recognition could read",
                "Take the photo again straight on, closer to the page and in good light, or paste the items as text.");
        }

        var words = page.Words.Select(w => new WordBox(w.Text, w.Left, w.Top, w.Width, w.Height, Confidence: w.Confidence)).ToList();
        var (ordered, twoColumns) = PageLayout.ReadingOrder(words);
        var lines = LayoutLines.ToSourceLines(ordered, page: null, fromOcr: true, ReasonsFor);

        var warnings = new List<AgendaParseWarning>
        {
            new(
                AgendaWarningCodes.OcrReview,
                engine.ReportsConfidence
                    ? "This agenda was read from an image with text recognition. Words it was unsure about are marked; check them against the original."
                    : "This agenda was read from an image with text recognition, which can misread letters and numbers and does not say how sure it is. Check the items against the original."),
        };
        if (twoColumns)
        {
            warnings.Add(new AgendaParseWarning(
                AgendaWarningCodes.MultiColumn,
                "The image seems to have two columns. The left column was read first, then the right one; reorder the items here if the agenda runs across."));
        }

        var structured = AgendaStructurer.Structure(lines, cancellationToken);
        return AgendaResults.Create(AgendaSourceKind.Image, options, structured, warnings, engine.Id);
    }

    internal static IReadOnlyList<string> ReasonsFor(LineBox line)
    {
        var reasons = new List<string>(1);
        if (OcrTextChecks.FindSuspicious(line.Text) is { } suspicious)
        {
            reasons.Add(UncertainReasons.OcrSuspicious(suspicious));
        }
        else if (line.Words.FirstOrDefault(w => w.Confidence < LowConfidence) is { } unsure)
        {
            reasons.Add(UncertainReasons.OcrLowConfidence(unsure.Text));
        }

        return reasons;
    }

    internal static async Task<IOcrEngine> ChooseEngineAsync(IReadOnlyList<IOcrEngine> engines, AgendaParseOptions options, CancellationToken cancellationToken)
    {
        if (engines.Count == 0)
        {
            throw AgendaErrors.OcrUnavailable(options, "no text recognition engine is set up.");
        }

        var ordered = engines
            .OrderBy(e => string.Equals(e.Id, options.OcrEngineId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
        string? firstReason = null;
        foreach (var engine in ordered)
        {
            var availability = await engine.GetAvailabilityAsync(options.OcrLanguage, cancellationToken).ConfigureAwait(false);
            if (availability.IsAvailable)
            {
                return engine;
            }

            firstReason ??= availability.Reason;
        }

        throw AgendaErrors.OcrUnavailable(options, firstReason ?? string.Create(CultureInfo.InvariantCulture, $"none of the {ordered.Count} engines can run."));
    }
}
