using Memento.Documents.Styling;

namespace Memento.Documents.Export;

/// <summary>
/// Page settings for <see cref="IPdfPrinter"/>, mirroring the print HTML's <c>@page</c> rule. The host passes them to
/// <c>CoreWebView2PrintSettings</c> (PageWidth/PageHeight and the margins in inches, ShouldPrintBackgrounds on,
/// ShouldPrintHeaderAndFooter off so Chromium adds no URL or date of its own).
/// </summary>
public sealed record PdfPrintOptions(
    double PageWidthInches,
    double PageHeightInches,
    double MarginTopInches,
    double MarginBottomInches,
    double MarginLeftInches,
    double MarginRightInches,
    bool PrintBackgrounds = true)
{
    public static PdfPrintOptions For(DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new PdfPrintOptions(
            StyleMetrics.PaperWidthInches(style.Paper),
            StyleMetrics.PaperHeightInches(style.Paper),
            StyleMetrics.MarginTopInches,
            StyleMetrics.MarginBottomInches,
            StyleMetrics.MarginSideInches,
            StyleMetrics.MarginSideInches);
    }
}
