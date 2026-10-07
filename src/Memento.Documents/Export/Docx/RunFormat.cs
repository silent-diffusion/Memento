using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Memento.Documents.Export.Docx;

/// <summary>Direct run formatting, written in the schema's element order.</summary>
internal sealed record RunFormat
{
    public string? StyleId { get; init; }

    public string? Font { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Caps { get; init; }

    /// <summary>Hex without <c>#</c>.</summary>
    public string? Color { get; init; }

    /// <summary>Letter spacing in twentieths of a point.</summary>
    public int? Spacing { get; init; }

    /// <summary>Size in points.</summary>
    public double? Size { get; init; }

    /// <summary>Background fill, hex without <c>#</c>.</summary>
    public string? Fill { get; init; }

    /// <summary>Pads the <see cref="Fill"/> with a border of the same colour.</summary>
    public bool Padded { get; init; }

    public bool Superscript { get; init; }

    public static RunFormat Plain { get; } = new();

    public IEnumerable<OpenXmlElement> Elements()
    {
        if (StyleId is not null)
        {
            yield return new RunStyle { Val = StyleId };
        }

        if (Font is not null)
        {
            yield return new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font, EastAsia = Font };
        }

        if (Bold)
        {
            yield return new Bold();
            yield return new BoldComplexScript();
        }

        if (Italic)
        {
            yield return new Italic();
            yield return new ItalicComplexScript();
        }

        if (Caps)
        {
            yield return new Caps();
        }

        if (Color is not null)
        {
            yield return new Color { Val = Color };
        }

        if (Spacing is { } spacing)
        {
            yield return new Spacing { Val = spacing };
        }

        if (Size is { } size)
        {
            yield return new FontSize { Val = DocxUnits.HalfPoints(size) };
            yield return new FontSizeComplexScript { Val = DocxUnits.HalfPoints(size) };
        }

        if (Fill is not null && Padded)
        {
            // A border in the fill colour pads the shading on every side, like the paper's pill.
            yield return new Border { Val = BorderValues.Single, Size = 4, Space = 1, Color = Fill };
        }

        if (Fill is not null)
        {
            yield return new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = Fill };
        }

        if (Superscript)
        {
            yield return new VerticalTextAlignment { Val = VerticalPositionValues.Superscript };
        }
    }

    public RunProperties? ToRunProperties()
    {
        var elements = Elements().ToList();
        return elements.Count == 0 ? null : new RunProperties(elements);
    }
}
