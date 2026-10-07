using System.Globalization;
using DocumentFormat.OpenXml.Wordprocessing;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda.OpenXml;

/// <summary>
/// Word's automatic numbering: resolves a paragraph's <c>w:numPr</c> (direct or from its style) to the marker Word
/// would show, counting per list and restarting deeper levels when a shallower one advances.
/// </summary>
internal sealed class WordNumbering
{
    private readonly Numbering? _numbering;
    private readonly WordStyles _styles;
    private readonly Dictionary<int, int?[]> _counters = [];

    public WordNumbering(Numbering? numbering, WordStyles styles)
    {
        _numbering = numbering;
        _styles = styles;
    }

    /// <summary>The list level (0-based) and marker of a numbered paragraph, or <c>null</c>.</summary>
    public (int Level, ListMarker? Marker)? Resolve(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var numPr = properties?.NumberingProperties;
        var numId = numPr?.NumberingId?.Val?.Value;
        var ilvl = numPr?.NumberingLevelReference?.Val?.Value;
        if (numId is null)
        {
            var fromStyle = _styles.NumberingOf(properties?.ParagraphStyleId?.Val?.Value);
            numId = fromStyle?.NumId;
            ilvl ??= fromStyle?.Level;
        }

        if (numId is null or 0)
        {
            return null;
        }

        var level = Math.Clamp(ilvl ?? 0, 0, 8);
        var definition = LevelDefinition(numId.Value, level);
        var format = definition?.NumberingFormat?.Val?.Value;
        if (format == NumberFormatValues.None)
        {
            return (level, null);
        }

        if (format == NumberFormatValues.Bullet)
        {
            return (level, new ListMarker(MarkerStyle.Bullet, null, null, BulletFamily: level % 3));
        }

        var counters = Counters(numId.Value);
        counters[level] = counters[level] is { } current ? current + 1 : StartOf(numId.Value, level);
        for (var deeper = level + 1; deeper < counters.Length; deeper++)
        {
            counters[deeper] = null;
        }

        var value = counters[level]!.Value;
        var levelText = definition?.LevelText?.Val?.Value ?? "%1.";
        var parts = levelText.Count(c => c == '%');
        if (parts > 1)
        {
            // "%1.%2." shows the parent numbers too: an outline number.
            var label = string.Join('.', Enumerable.Range(0, level + 1).Select(l => (counters[l] ?? StartOf(numId.Value, l)).ToString(CultureInfo.InvariantCulture)));
            return (level, new ListMarker(MarkerStyle.Outline, value, label, level + 1));
        }

        return (level, StyleOf(format, value));
    }

    private static ListMarker StyleOf(NumberFormatValues? format, int value)
    {
        if (format == NumberFormatValues.LowerLetter)
        {
            return new ListMarker(MarkerStyle.LowerLetter, value, ((char)('a' + ((value - 1) % 26))).ToString());
        }

        if (format == NumberFormatValues.UpperLetter)
        {
            return new ListMarker(MarkerStyle.UpperLetter, value, ((char)('A' + ((value - 1) % 26))).ToString());
        }

        if (format == NumberFormatValues.LowerRoman)
        {
            return new ListMarker(MarkerStyle.LowerRoman, value, MarkerParser.ToRoman(value, upper: false));
        }

        if (format == NumberFormatValues.UpperRoman)
        {
            return new ListMarker(MarkerStyle.UpperRoman, value, MarkerParser.ToRoman(value, upper: true));
        }

        return new ListMarker(MarkerStyle.Decimal, value, value.ToString(CultureInfo.InvariantCulture));
    }

    private int?[] Counters(int numId)
    {
        if (!_counters.TryGetValue(numId, out var counters))
        {
            counters = new int?[9];
            _counters[numId] = counters;
        }

        return counters;
    }

    private int StartOf(int numId, int level)
    {
        var instance = Instance(numId);
        var overrideStart = instance?.Elements<LevelOverride>()
            .FirstOrDefault(o => o.LevelIndex?.Value == level)?.StartOverrideNumberingValue?.Val?.Value;
        return overrideStart ?? LevelDefinition(numId, level)?.StartNumberingValue?.Val?.Value ?? 1;
    }

    private NumberingInstance? Instance(int numId) =>
        _numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);

    private Level? LevelDefinition(int numId, int level)
    {
        var instance = Instance(numId);
        if (instance is null)
        {
            return null;
        }

        var overridden = instance.Elements<LevelOverride>().FirstOrDefault(o => o.LevelIndex?.Value == level)?.Level;
        if (overridden is not null)
        {
            return overridden;
        }

        var abstractId = instance.AbstractNumId?.Val?.Value;
        var abstractNum = _numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
        return abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
    }
}
