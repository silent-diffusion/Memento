using System.Globalization;

namespace Memento.Documents.Export.Docx;

/// <summary>Unit conversions for WordprocessingML.</summary>
internal static class DocxUnits
{
    /// <summary>Points to half-points (<c>w:sz</c>).</summary>
    public static string HalfPoints(double points) => ((int)Math.Round(points * 2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Points to twentieths of a point (spacing, indents).</summary>
    public static int Twips(double points) => (int)Math.Round(points * 20, MidpointRounding.AwayFromZero);

    public static string TwipsText(double points) => Twips(points).ToString(CultureInfo.InvariantCulture);

    public static int InchesToTwips(double inches) => (int)Math.Round(inches * 1440, MidpointRounding.AwayFromZero);

    /// <summary>"#1F3A5F" to "1F3A5F".</summary>
    public static string Hex(string cssColor) => cssColor.TrimStart('#').ToUpperInvariant();
}
