using System.Globalization;
using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>Finds list markers (numbers, letters, Roman numerals, bullets, checkboxes) and time prefixes at the start of a line.</summary>
internal static partial class MarkerParser
{
    private const int MarkerWindow = 256;

    /// <summary>"10:00 a.m. until 11:30 p.m." is about 30 characters; a cell much longer is not a time.</summary>
    private const int MaxTimeLength = 64;

    private const string TimeExpression =
        @"(?:[0-9]{1,2}[:.h][0-9]{2}|[0-9]{1,2})(?:\s*[ap]\.?\s?m\.?)?(?:\s*(?:-|–|—|to|until)\s*(?:[0-9]{1,2}[:.h][0-9]{2}|[0-9]{1,2})(?:\s*[ap]\.?\s?m\.?)?)?";

    public static bool TryParseMarker(string text, out ListMarker marker, out string rest)
    {
        marker = new ListMarker(MarkerStyle.None, null, null);
        rest = text;
        if (text.Length < 2)
        {
            return false;
        }

        var head = Head(text);
        var match = RegexGuard.Match(CheckboxPattern(), head);
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

        match = RegexGuard.Match(BulletPattern(), head);
        if (match.Success)
        {
            marker = new ListMarker(MarkerStyle.Bullet, null, null, BulletFamily: BulletFamily(match.Groups["g"].Value));
            rest = text[match.Length..];
            return true;
        }

        match = RegexGuard.Match(OutlinePattern(), head);
        if (match.Success)
        {
            var label = match.Groups["v"].Value;
            var parts = label.Split('.');
            marker = new ListMarker(MarkerStyle.Outline, int.Parse(parts[^1], CultureInfo.InvariantCulture), label, parts.Length);
            rest = text[match.Length..];
            return true;
        }

        match = RegexGuard.Match(DecimalPattern(), head);
        if (match.Success)
        {
            var label = match.Groups["v"].Value;
            marker = new ListMarker(MarkerStyle.Decimal, int.Parse(label, CultureInfo.InvariantCulture), label);
            rest = text[match.Length..];
            return true;
        }

        match = RegexGuard.Match(RomanPattern(), head);
        if (match.Success && RomanValue(match.Groups["v"].Value) is { } roman && IsSingleCase(match.Groups["v"].Value))
        {
            var label = match.Groups["v"].Value;
            marker = new ListMarker(char.IsUpper(label[0]) ? MarkerStyle.UpperRoman : MarkerStyle.LowerRoman, roman, label);
            rest = text[match.Length..];
            return true;
        }

        match = RegexGuard.Match(LetterPattern(), head);
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
        var match = RegexGuard.Match(TimePrefixPattern(), Head(text));
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
        // A time ends in a digit, or in "m" or "." of a meridiem; anything else cannot end in one.
        if (text.Length is < 5 or > RegexGuard.MaxLineLength || !(char.IsAsciiDigit(text[^1]) || text[^1] is 'm' or 'M' or '.'))
        {
            return false;
        }

        var match = RegexGuard.Match(TrailingTimePattern(), text);
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
        return trimmed.Length <= MaxTimeLength && RegexGuard.IsMatch(WholeTimePattern(), trimmed) && IsValidTime(trimmed);
    }

    /// <summary>
    /// The start of a line, where markers and time prefixes are looked for: they are a few characters long, and a
    /// pattern never needs to see a megabyte of whitespace after them.
    /// </summary>
    private static string Head(string text) => text.Length > MarkerWindow ? text[..MarkerWindow] : text;

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
        var match = RegexGuard.Match(TimePartPattern(), text);
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

    [GeneratedRegex(@"^(?:[-*+]\s+)?(?:\[[ xX✓✔]\]|[☐☑☒])\s*(?=\S)", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex CheckboxPattern();

    [GeneratedRegex(@"^(?:(?<g>[-*+])\s+|(?<g>[–—])\s+|(?<g>o)(?:\t|\s{2,})|(?<g>[•●○◦▪▫■□►▸‣⁃·»→✓✔])\s*)(?=\S)", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^(?<v>[0-9]{1,3}(?:\.[0-9]{1,3})+)\.?(?:\)\s*|\s+|(?=\p{L}))(?=\S)", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex OutlinePattern();

    [GeneratedRegex(@"^(?:\((?<v>[0-9]{1,3})\)|\#(?<v>[0-9]{1,3})[.):]?|(?<v>[0-9]{1,3})(?:[.)\]:]|\s+[-–—](?=\s)))\s*(?=\S)", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex DecimalPattern();

    [GeneratedRegex(@"^\(?(?<v>[ivxIVX]{2,5})[.)]\s+(?=\S)", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex RomanPattern();

    [GeneratedRegex(@"^\(?(?<v>[A-Za-z])[.)]\s+(?=\S)", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex LetterPattern();

    [GeneratedRegex(@"^(?<t>" + TimeExpression + @")(?:\s*[-–—:|•·]\s*|\s+)(?=\S)", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex TimePrefixPattern();

    // The leader is atomic (a time never starts with whitespace or a dot, so its greedy match is the only one that can
    // work) and a dot leader must start its run of dots: the old "\s*(?:\.{3,}|…+|\t+|\s{3,})\s*" let three
    // quantifiers share one run of spaces or dots, which is cubic on a long run with no time after it.
    [GeneratedRegex(@"^(?<text>.*?\S)(?>\s*(?<![.…])(?:\.{3,}|…+)\s*|\s*\t\s*|\s{3,})(?<t>" + TimeExpression + @")$", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex TrailingTimePattern();

    [GeneratedRegex(@"^" + TimeExpression + @"$", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex WholeTimePattern();

    [GeneratedRegex(@"^(?<h>[0-9]{1,2})(?:(?<s>[:.h])(?<m>[0-9]{2}))?(?:\s*(?<ap>[ap]\.?\s?m\.?))?", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex TimePartPattern();

    [GeneratedRegex(@"\s+", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex WhitespacePattern();
}
