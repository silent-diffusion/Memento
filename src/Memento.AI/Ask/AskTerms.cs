using System.Globalization;
using System.Text;

namespace Memento.AI.Ask;

/// <summary>
/// The words a question and a transcript section are compared by (2.9.0 Ask): lower-case runs of letters and digits,
/// without the most common English function words and single letters, with a light suffix fold ("decided", "decides"
/// and "deciding" all read as "decid") so a question's wording need not match the transcript's exactly.
/// </summary>
public static class AskTerms
{
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "a", "about", "after", "all", "also", "am", "an", "and", "any", "are", "as", "at", "be", "because", "been", "but", "by", "can", "could",
        "did", "do", "does", "for", "from", "had", "has", "have", "he", "her", "him", "his", "how", "i", "if", "in", "into", "is", "it", "its",
        "just", "me", "my", "no", "not", "of", "on", "or", "our", "she", "so", "some", "than", "that", "the", "their", "them", "then", "there",
        "these", "they", "this", "those", "to", "up", "us", "was", "we", "were", "what", "when", "where", "which", "who", "whom", "why", "will",
        "with", "would", "you", "your", "yes", "yeah", "okay", "ok", "um", "uh", "like", "get", "got", "say", "said", "tell", "meeting", "recording",
    };

    /// <summary>The comparable terms of <paramref name="text"/>, in order, repeats kept.</summary>
    public static IReadOnlyList<string> Of(string? text)
    {
        var terms = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return terms;
        }

        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length == 0)
            {
                return;
            }

            var value = word.ToString();
            word.Clear();
            if (value.Length < 2 || Stop.Contains(value))
            {
                return;
            }

            terms.Add(Fold(value));
        }

        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
            else
            {
                // Any other character ends the word: "team's" reads as "team" (the lone "s" is dropped).
                Flush();
            }
        }

        Flush();
        return terms;
    }

    /// <summary>A light English suffix fold, only on words long enough that the stem stays meaningful.</summary>
    public static string Fold(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length <= 4 || word.Any(char.IsDigit))
        {
            return word;
        }

        foreach (var suffix in Suffixes)
        {
            if (word.Length - suffix.Length >= 4 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    private static readonly string[] Suffixes = ["ings", "ing", "ies", "ied", "es", "ed", "ly", "s", "e"];
}
