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
    private readonly Dictionary<int, NumberingInstance> _instances = [];
    private readonly Dictionary<int, AbstractNum> _abstracts = [];
    private readonly Dictionary<(int NumId, int Level), Level?> _levels = [];
    private readonly WordStyles _styles;
    private readonly Dictionary<int, int?[]> _counters = [];

    public WordNumbering(Numbering? numbering, WordStyles styles)
    {
        _styles = styles;
        foreach (var instance in numbering?.Elements<NumberingInstance>() ?? [])
        {
            if (OpenXmlValues.Int(instance.NumberID) is { } id)
            {
                _instances.TryAdd(id, instance);
            }
        }

        foreach (var abstractNum in numbering?.Elements<AbstractNum>() ?? [])
        {
            if (OpenXmlValues.Int(abstractNum.AbstractNumberId) is { } id)
            {
                _abstracts.TryAdd(id, abstractNum);
            }
        }
    }

    /// <summary>The list level (0-based) and marker of a numbered paragraph, or <c>null</c>.</summary>
    public (int Level, ListMarker? Marker)? Resolve(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var numPr = properties?.NumberingProperties;
        var numId = OpenXmlValues.Int(numPr?.NumberingId?.Val);
        var ilvl = OpenXmlValues.Int(numPr?.NumberingLevelReference?.Val);
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
        var format = OpenXmlValues.Enum(definition?.NumberingFormat?.Val);
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
        var overrideStart = OpenXmlValues.Int(Override(numId, level)?.StartOverrideNumberingValue?.Val);
        return overrideStart ?? OpenXmlValues.Int(LevelDefinition(numId, level)?.StartNumberingValue?.Val) ?? 1;
    }

    private LevelOverride? Override(int numId, int level) =>
        _instances.TryGetValue(numId, out var instance)
            ? instance.Elements<LevelOverride>().FirstOrDefault(o => OpenXmlValues.Int(o.LevelIndex) == level)
            : null;

    private Level? LevelDefinition(int numId, int level)
    {
        // Looked up once per list and level: a damaged file can repeat definitions thousands of times.
        if (_levels.TryGetValue((numId, level), out var cached))
        {
            return cached;
        }

        Level? definition = null;
        if (_instances.TryGetValue(numId, out var instance))
        {
            definition = Override(numId, level)?.Level;
            if (definition is null &&
                OpenXmlValues.Int(instance.AbstractNumId?.Val) is { } abstractId &&
                _abstracts.TryGetValue(abstractId, out var abstractNum))
            {
                definition = abstractNum.Elements<Level>().FirstOrDefault(l => OpenXmlValues.Int(l.LevelIndex) == level);
            }
        }

        _levels[(numId, level)] = definition;
        return definition;
    }
}
