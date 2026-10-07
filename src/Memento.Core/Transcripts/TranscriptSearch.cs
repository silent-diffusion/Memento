using System.Globalization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// <c>transcript.search</c>: case-insensitive, and a match must begin at a word boundary ("plan" finds "planning"
/// and "Plan," but not "explain"). Each matching segment is listed once with a snippet around the first hit.
/// </summary>
public static class TranscriptSearch
{
    public const int MaxQueryLength = 200;
    private const int Before = 40;
    private const int After = 80;

    public static IReadOnlyList<TranscriptMatch> Find(IReadOnlyList<TranscriptSegment> segments, string query)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var needle = (query ?? string.Empty).Trim();
        if (needle.Length == 0)
        {
            return [];
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var matches = new List<TranscriptMatch>();
        foreach (var segment in segments)
        {
            var index = FindAtWordStart(compare, segment.Text, needle);
            if (index >= 0)
            {
                matches.Add(new TranscriptMatch(segment.Id, segment.Start, Snippet(segment.Text, index, needle.Length)));
            }
        }

        return matches;
    }

    /// <summary>The text around <paramref name="index"/>, cut at word boundaries, with "…" where it was shortened.</summary>
    public static string Snippet(string text, int index, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        var from = Math.Max(0, index - Before);
        if (from > 0)
        {
            var space = text.IndexOf(' ', from);
            from = space >= 0 && space < index ? space + 1 : from;
        }

        var to = Math.Min(text.Length, index + length + After);
        if (to < text.Length)
        {
            var space = text.LastIndexOf(' ', to - 1);
            to = space > index + length ? space : to;
        }

        return (from > 0 ? "…" : string.Empty) + text[from..to].Trim() + (to < text.Length ? "…" : string.Empty);
    }

    private static int FindAtWordStart(CompareInfo compare, string text, string needle)
    {
        var start = 0;
        while (start <= text.Length - needle.Length)
        {
            var index = compare.IndexOf(text, needle, start, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (index < 0)
            {
                return -1;
            }

            if (index == 0 || !char.IsLetterOrDigit(text[index - 1]))
            {
                return index;
            }

            start = index + 1;
        }

        return -1;
    }
}
