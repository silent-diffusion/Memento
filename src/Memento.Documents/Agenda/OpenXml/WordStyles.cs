using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Memento.Documents.Agenda.OpenXml;

/// <summary>Paragraph styles of a Word document: headings, the title, list styles and numbering inherited through <c>basedOn</c>.</summary>
internal sealed partial class WordStyles
{
    private readonly Dictionary<string, Style> _styles;

    public WordStyles(Styles? styles)
    {
        _styles = (styles?.Elements<Style>() ?? [])
            .Where(s => s.StyleId?.Value is not null)
            .GroupBy(s => s.StyleId!.Value!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The heading level (1-9) of a paragraph from its outline level or heading style; <c>null</c> for body text.</summary>
    public int? HeadingLevel(Paragraph paragraph)
    {
        var direct = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (direct is { } outline and < 9)
        {
            return outline + 1;
        }

        foreach (var style in Chain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
        {
            var level = style.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
            if (level is { } styleOutline and < 9)
            {
                return styleOutline + 1;
            }

            var match = HeadingNamePattern().Match(Name(style));
            if (match.Success)
            {
                return int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    public bool IsTitle(Paragraph paragraph) =>
        Chain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value).Any(s => Name(s).Equals("title", StringComparison.OrdinalIgnoreCase));

    /// <summary>The level a list style implies ("List Bullet 2" is level 1) when it carries no numbering of its own.</summary>
    public int? ListStyleLevel(Paragraph paragraph)
    {
        foreach (var style in Chain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
        {
            var match = ListNamePattern().Match(Name(style));
            if (match.Success)
            {
                return match.Groups["n"].Success ? int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1 : 0;
            }
        }

        return null;
    }

    /// <summary>Whether the paragraph's style is a bulleted list style ("List Bullet").</summary>
    public bool IsBulletStyle(Paragraph paragraph) =>
        Chain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value).Any(s => Name(s).Contains("bullet", StringComparison.OrdinalIgnoreCase));

    public (int NumId, int? Level)? NumberingOf(string? styleId)
    {
        foreach (var style in Chain(styleId))
        {
            var numPr = style.StyleParagraphProperties?.NumberingProperties;
            if (numPr?.NumberingId?.Val?.Value is { } numId)
            {
                return (numId, numPr.NumberingLevelReference?.Val?.Value);
            }
        }

        return null;
    }

    private static string Name(Style style) => style.StyleName?.Val?.Value ?? style.StyleId?.Value ?? string.Empty;

    private IEnumerable<Style> Chain(string? styleId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (styleId is not null && seen.Add(styleId) && _styles.TryGetValue(styleId, out var style))
        {
            yield return style;
            styleId = style.BasedOn?.Val?.Value;
        }
    }

    [GeneratedRegex(@"^heading\s?(?<n>[1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingNamePattern();

    [GeneratedRegex(@"^list\s?(?:bullet|number|continue|paragraph)(?:\s?(?<n>[2-9]))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ListNamePattern();
}
