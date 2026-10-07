namespace Memento.Documents.Styling;

/// <summary>
/// The concrete values behind a <see cref="DocumentStyle"/>, shared by the HTML renderer and the Word exporter so the
/// preview, the viewer, Word and PDF agree. Screen sizes are DESIGN.md §13's (11 / 12 / 13.5 px); print sizes are the same
/// proportions in points (10 / 11 / 12.5 pt), the size a printed page needs.
/// </summary>
public static class StyleMetrics
{
    /// <summary>Body text, fixed (DESIGN.md §13 "Body text (fixed Ink)").</summary>
    public const string Ink = "#1D1C1A";

    /// <summary>The meta line under the title (DESIGN.md §12).</summary>
    public const string MetaInk = "#6B6861";

    /// <summary>Hairlines: table rows, lines between sections, the running header's rule.</summary>
    public const string Hairline = "#E2DFD8";

    /// <summary>Running header and page numbers.</summary>
    public const string FaintInk = "#8A867E";

    /// <summary>Timestamp and participant chips.</summary>
    public const string ChipFill = "#EDEBE6";

    public const string ChipInk = "#5E5B55";

    /// <summary>The bar beside quotes.</summary>
    public const string QuoteBar = "#B9B5AC";

    public const string SansStack = "'Segoe UI', system-ui, -apple-system, 'Helvetica Neue', Arial, sans-serif";

    public const string SerifStack = "Georgia, Cambria, 'Times New Roman', serif";

    public const string MonoStack = "'JetBrains Mono', Consolas, 'Cascadia Mono', monospace";

    /// <summary>Word fonts for the two typefaces (both ship with Windows; Word substitutes elsewhere).</summary>
    public const string SansWordFont = "Segoe UI";

    public const string SerifWordFont = "Georgia";

    public const string MonoWordFont = "Consolas";

    /// <summary>Page margins in inches, the same in print HTML and Word.</summary>
    public const double MarginTopInches = 0.9;

    public const double MarginBottomInches = 0.9;

    public const double MarginSideInches = 1.0;

    /// <summary>Distance of the running header and the page number from the page edge, in inches.</summary>
    public const double HeaderFooterInches = 0.45;

    public static double ScreenBasePx(this DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style.BaseSize switch
        {
            BaseSize.Small => 11,
            BaseSize.Large => 13.5,
            _ => 12,
        };
    }

    public static double PrintBasePt(this DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style.BaseSize switch
        {
            BaseSize.Small => 10,
            BaseSize.Large => 12.5,
            _ => 11,
        };
    }

    /// <summary>Space between sections on screen: 10 / 18 / 28 px.</summary>
    public static double SpacingPx(this DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style.Spacing switch
        {
            Spacing.Tight => 10,
            Spacing.Airy => 28,
            _ => 18,
        };
    }

    /// <summary>Space between sections in print: the screen value in the same ratio as the base size (×11/12), in points.</summary>
    public static double SpacingPt(this DocumentStyle style) => Math.Round(style.SpacingPx() * 11 / 12, 1);

    public static string HeadingHex(this DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return HeadingHex(style.HeadingColor);
    }

    public static string HeadingHex(HeadingColor color) => color switch
    {
        HeadingColor.Navy => "#1F3A5F",
        HeadingColor.Forest => "#2F6B4F",
        HeadingColor.Burgundy => "#7A2E2E",
        _ => "#1D1C1A",
    };

    /// <summary>The soft tint paired with each heading colour, used for table header fills.</summary>
    public static string TintHex(HeadingColor color) => color switch
    {
        HeadingColor.Navy => "#D9E1EC",
        HeadingColor.Forest => "#DCE9E1",
        HeadingColor.Burgundy => "#EEDCDC",
        _ => "#E8E6E0",
    };

    public static string TintHex(this DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return TintHex(style.HeadingColor);
    }

    public static string CssStack(Typeface typeface) => typeface == Typeface.Serif ? SerifStack : SansStack;

    public static string WordFont(Typeface typeface) => typeface == Typeface.Serif ? SerifWordFont : SansWordFont;

    public static double PaperWidthInches(PaperSize paper) => paper == PaperSize.A4 ? 210 / 25.4 : 8.5;

    public static double PaperHeightInches(PaperSize paper) => paper == PaperSize.A4 ? 297 / 25.4 : 11;

    /// <summary>Page size in twentieths of a point (Word): Letter 12240 × 15840, A4 11906 × 16838.</summary>
    public static (uint Width, uint Height) PaperTwips(PaperSize paper) => paper == PaperSize.A4 ? (11906u, 16838u) : (12240u, 15840u);

    /// <summary>The CSS <c>@page size</c> keyword.</summary>
    public static string CssPageSize(PaperSize paper) => paper == PaperSize.A4 ? "A4" : "letter";

    public static string PaperName(PaperSize paper) => paper == PaperSize.A4 ? "A4" : "Letter";
}
