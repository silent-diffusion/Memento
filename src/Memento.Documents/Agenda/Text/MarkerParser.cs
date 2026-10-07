using System.Globalization;
using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>Finds list markers (numbers, letters, Roman numerals, bullets, checkboxes) and time prefixes at the start of a line.</summary>
internal static partial class MarkerParser
{
    private const string TimeExpression =
        @"(?:\d{1,2}[:.h]\d{2}|\d{1,2})(?:\s*[ap]\.?\s?m\.?)?(?:\s*(?:-|–|—|to|until)\s*(?:\d{1,2}[:.h]\d{2}|\d{1,2})(?:\s*[ap]\.?\s?m\.?)?)?";

    public static bool TryParseMarker(string text, out ListMarker marker, out string rest)
    {
        marker = new ListMarker(MarkerStyle.None, null, null);
        rest = text;
        if (text.Length < 2)
        {
            return false;
        }

        var match = CheckboxPattern().Match(text);
        if (match.Success)
        {
            marker = new ListMarker(MarkerStyle.Checkbox, null, null);
            rest = text[match.Length..];
            return true;
        }

        if (TryParseTimePrefix(text, out _, out _))
        {
            return false;
        }

        match = BulletPattern().Match(text);
        if (match.Success)
        {
            marker = new ListMarker(MarkerStyle.Bullet, null, null, BulletFamily: BulletFamily(match.Groups["g"].Value));
            rest = text[match.Length..];
            return true;
        }

        match = OutlinePattern().Match(text);
        if (match.Success)
        {
            var label = match.Groups["v"].Value;
            var parts = label.Split('.');
            marker = new ListMarker(MarkerStyle.Outline, int.Parse(parts[^1], CultureInfo.InvariantCulture), label, parts.Length);
            rest = text[match.Length..];
            return true;
        }

        match = DecimalPattern().Match(text);
        if (match.Success)
        {
            var label = match.Groups["v"].Value;
            marker = new ListMarker(MarkerStyle.Decimal, int.Parse(label, CultureInfo.InvariantCulture), label);
            rest = text[match.Length..];
            return true;
        }

        match = RomanPattern().Match(text);
        if (match.Success && RomanValue(match.Groups["v"].Value) is { } roman && IsSingleCase(match.Groups["v"].Value))
        {
            var label = match.Groups["v"].Value;
            marker = new ListMarker(char.IsUpper(label[0]) ? MarkerStyle.UpperRoman : MarkerStyle.LowerRoman, roman, label);
            rest = text[match.Length..];
            return true;
        }

        match = LetterPattern().Match(text);
        if (match.Success)
        {
            var letter = match.Groups["v"].Value[0];
            var upper = char.IsUpper(letter);
            marker = new ListMarker(
                upper ? MarkerStyle.UpperLetter : MarkerStyle.LowerLetter,
                char.ToLowerInvariant(letter) - 'a' + 1,
                letter.ToString(),
                AlternateRomanValue: RomanValue(letter.ToString()));
            rest = text[match.Length..];
            return true;
        }

        return false;
    }

    /// <summary>A leading time such as "10:00 –", "9:30-10:15", "2 pm" or "09.15"; <paramref name="rest"/> is the text after it.</summary>
    public static bool TryParseTimePrefix(string text, out string time, out string rest)
    {
        time = string.Empty;
        rest = text;
        var match = TimePrefixPattern().Match(text);
        if (!match.Success || !IsValidTime(match.Groups["t"].Value))
        {
            return false;
        }

        time = NormalizeTime(match.Groups["t"].Value);
        rest = text[match.Length..];
        return true;
    }

    /// <summary>A time at the end after dot leaders or a wide gap: "Welcome ........ 10:00".</summary>
    public static bool TryParseTrailingTime(string text, out string time, out string rest)
    {
        time = string.Empty;
        rest = text;
        var match = TrailingTimePattern().Match(text);
        if (!match.Success || !IsValidTime(match.Groups["t"].Value))
        {
            return false;
        }

        time = NormalizeTime(match.Groups["t"].Value);
        rest = match.Groups["text"].Value;
        return true;
    }

    /// <summary>Whether a whole cell is a time or time range.</summary>
    public static bool IsTime(string text)
    {
        var trimmed = text.Trim();
        return WholeTimePattern().IsMatch(trimmed) && IsValidTime(trimmed);
    }

    public static int? RomanValue(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var lower = text.ToLowerInvariant();
        string[] numerals = ["i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x", "xi", "xii", "xiii", "xiv", "xv", "xvi", "xvii", "xviii", "xix", "xx"];
        var index = Array.IndexOf(numerals, lower);
        return index < 0 ? null : index + 1;
    }

    public static string ToRoman(int value, bool upper)
    {
        string[] numerals = ["i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x", "xi", "xii", "xiii", "xiv", "xv", "xvi", "xvii", "xviii", "xix", "xx"];
        var text = value is >= 1 and <= 20 ? numerals[value - 1] : value.ToString(CultureInfo.InvariantCulture);
        return upper ? text.ToUpperInvariant() : text;
    }

    private static bool IsSingleCase(string text) => text.All(char.IsUpper) || text.All(char.IsLower);

    private static int BulletFamily(string glyph) => glyph switch
    {
        "○" or "◦" or "o" or "–" or "—" or "»" or "⁃" or "‣" or "" => 1,
        "▪" or "▫" or "■" or "□" or "►" or "▸" or "" or "" => 2,
        _ => 0,
    };

    private static bool IsValidTime(string text)
    {
        var match = TimePartPattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var hasMinutes = match.Groups["m"].Success;
        var hasMeridiem = match.Groups["ap"].Success;
        if (!hasMinutes && !hasMeridiem)
        {
            return false;
        }

        if (hour > (hasMeridiem ? 12 : 23))
        {
            return false;
        }

        if (hasMinutes)
        {
            var minutes = match.Groups["m"].Value;
            if (int.Parse(minutes, CultureInfo.InvariantCulture) > 59)
            {
                return false;
            }

            // "1.2" is an outline number, "10.30" a time: a dotted time needs a usual minute value, a leading zero, a
            // meridiem or a range.
            if (match.Groups["s"].Value == ".")
            {
                var isRange = text.Length > match.Length;
                var leadingZero = match.Groups["h"].Value.Length == 2 && match.Groups["h"].Value[0] == '0';
                if (!(hasMeridiem || isRange || leadingZero || minutes is "00" or "15" or "30" or "45"))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string NormalizeTime(string time) => WhitespacePattern().Replace(time.Trim(), " ");

    [GeneratedRegex(@"^(?:[-*+]\s+)?(?:\[[ xX✓✔]\]|[☐☑☒])\s*(?=\S)")]
    private static partial Regex CheckboxPattern();

    [GeneratedRegex(@"^(?:(?<g>[-*+])\s+|(?<g>[–—])\s+|(?<g>o)(?:\t|\s{2,})|(?<g>[•●○◦▪▫■□►▸‣⁃·»→✓✔])\s*)(?=\S)")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^(?<v>\d{1,3}(?:\.\d{1,3})+)\.?(?:\)\s*|\s+|(?=\p{L}))(?=\S)")]
    private static partial Regex OutlinePattern();

    [GeneratedRegex(@"^(?:\((?<v>\d{1,3})\)|\#(?<v>\d{1,3})[.):]?|(?<v>\d{1,3})(?:[.)\]:]|\s+[-–—](?=\s)))\s*(?=\S)")]
    private static partial Regex DecimalPattern();

    [GeneratedRegex(@"^\(?(?<v>[ivxIVX]{2,5})[.)]\s+(?=\S)")]
    private static partial Regex RomanPattern();

    [GeneratedRegex(@"^\(?(?<v>[A-Za-z])[.)]\s+(?=\S)")]
    private static partial Regex LetterPattern();

    [GeneratedRegex(@"^(?<t>" + TimeExpression + @")(?:\s*[-–—:|•·]\s*|\s+)(?=\S)", RegexOptions.IgnoreCase)]
    private static partial Regex TimePrefixPattern();

    [GeneratedRegex(@"^(?<text>.*?\S)\s*(?:\.{3,}|…+|\t+|\s{3,})\s*(?<t>" + TimeExpression + @")$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTimePattern();

    [GeneratedRegex(@"^" + TimeExpression + @"$", RegexOptions.IgnoreCase)]
    private static partial Regex WholeTimePattern();

    [GeneratedRegex(@"^(?<h>\d{1,2})(?:(?<s>[:.h])(?<m>\d{2}))?(?:\s*(?<ap>[ap]\.?\s?m\.?))?", RegexOptions.IgnoreCase)]
    private static partial Regex TimePartPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
