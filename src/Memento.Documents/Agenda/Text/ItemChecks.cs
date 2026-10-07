using System.Globalization;
using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>The per-item checks that mark an item uncertain: merged lines, very long or very short text.</summary>
internal static partial class ItemChecks
{
    public static IEnumerable<string> Check(string text, ListMarker? marker, bool headingMergeSuspected)
    {
        if (headingMergeSuspected)
        {
            yield return UncertainReasons.HeadingMerged;
        }
        else
        {
            var midBullets = MidLineBulletPattern().Matches(text).Count;
            if (midBullets >= 2)
            {
                yield return UncertainReasons.SeveralItemsMerged;
            }
            else if (midBullets == 1 || HeadingThenBulletPattern().IsMatch(text))
            {
                yield return UncertainReasons.HeadingMerged;
            }
            else if (marker is { Style: MarkerStyle.Decimal or MarkerStyle.Outline, Value: { } value })
            {
                var next = (value + 1).ToString(CultureInfo.InvariantCulture);
                foreach (Match match in NextNumberPattern().Matches(text))
                {
                    if (match.Groups["n"].Value == next)
                    {
                        yield return UncertainReasons.NumberedItemsMerged(marker.OutlinePrefix is { } prefix ? $"{prefix}.{next}" : next);
                        break;
                    }
                }
            }
        }

        var length = text.Length;
        if (length > AgendaLimits.LongItemLength)
        {
            yield return UncertainReasons.TooLong(length);
        }
        else if (length < AgendaLimits.ShortItemLength)
        {
            yield return UncertainReasons.TooShort;
        }
    }

    [GeneratedRegex(@"\S\s+[•●▪■◦►▸‣]\s+\S")]
    private static partial Regex MidLineBulletPattern();

    [GeneratedRegex(@"^[^:]{2,60}:\s*(?:[-*–]|\d{1,2}[.)])\s+\S")]
    private static partial Regex HeadingThenBulletPattern();

    [GeneratedRegex(@"\S\s+(?<n>\d{1,3})[.)]\s+\p{Lu}")]
    private static partial Regex NextNumberPattern();
}
