using System.Text;
using System.Text.RegularExpressions;

namespace Memento.Generation.Generation;

/// <summary>
/// Text comparison the pipeline does in code (ENGINE-NOTES.md §H): normalised containment for quotes (case, punctuation
/// and spacing ignored; "…" or "..." may join pieces of one quote), word-set similarity for deduplication.
/// </summary>
public static partial class TextMatch
{
    /// <summary>Lower case, letters and digits only, single spaces.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var space = true;
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (c == '\'' || c == '’')
            {
                // "don't" and "dont" match.
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>The quote's pieces (split at an ellipsis), normalised, without empty ones.</summary>
    public static IReadOnlyList<string> QuotePieces(string? quote) =>
        Ellipsis().Split(quote ?? string.Empty).Select(Normalize).Where(p => p.Length > 0).ToList();

    /// <summary>Words each piece of a quote joined by an ellipsis must have, so scattered words cannot pass as a quote.</summary>
    public const int MinWordsPerPiece = 3;

    /// <summary>
    /// The pieces of <paramref name="quote"/> occur in <paramref name="text"/> in their order, without overlapping
    /// (normalised, on word boundaries). A quote of several pieces needs at least <see cref="MinWordsPerPiece"/> words in
    /// each, so "we … not … ship" cannot be assembled from words spread over a line.
    /// </summary>
    public static bool QuoteIn(string? quote, string? text)
    {
        var pieces = QuotePieces(quote);
        if (pieces.Count == 0)
        {
            return false;
        }

        if (pieces.Count > 1 && pieces.Any(p => p.Split(' ').Length < MinWordsPerPiece))
        {
            return false;
        }

        var haystack = " " + Normalize(text) + " ";
        var from = 0;
        foreach (var piece in pieces)
        {
            var at = haystack.IndexOf(" " + piece + " ", from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            // The next piece may start at the space this one ends with.
            from = at + piece.Length + 1;
        }

        return true;
    }

    /// <summary>Share of the quote's words that occur in <paramref name="text"/> (for near-verbatim quotes), 0..1.</summary>
    public static double QuoteCoverage(string? quote, string? text)
    {
        var words = Normalize(quote).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return 0;
        }

        var have = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return (double)words.Count(have.Contains) / words.Length;
    }

    /// <summary>The content words (longer than three letters) of a text, lightly stemmed ("flags" and "flag" match).</summary>
    public static HashSet<string> Words(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).Select(Stem).ToHashSet(StringComparer.Ordinal);

    private static string Stem(string word) =>
        word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal) ? word[..^3]
        : word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal) ? word[..^2]
        : word.Length > 4 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1]
        : word;

    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count + b.Count == 0)
        {
            return 0;
        }

        var shared = a.Count(b.Contains);
        return (double)shared / (a.Count + b.Count - shared);
    }

    /// <summary><paramref name="phrase"/> occurs in <paramref name="text"/> as whole words (normalised).</summary>
    public static bool ContainsPhrase(string? text, string? phrase)
    {
        var needle = Normalize(phrase);
        return needle.Length > 0 && (" " + Normalize(text) + " ").Contains(" " + needle + " ", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\.\.\.|…", RegexOptions.CultureInvariant)]
    private static partial Regex Ellipsis();
}
