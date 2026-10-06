using System.Text;

namespace Memento.Core.Library;

/// <summary>
/// Turns what the user typed into an FTS5 match expression: every word must match, each by prefix.
/// Words are quoted, so FTS operators and punctuation in the search box are taken literally.
/// </summary>
public static class FtsQuery
{
    public const int MaxLength = 200;
    private const int MaxTerms = 16;

    /// <summary>The match expression, or <c>null</c> when nothing searchable was typed.</summary>
    public static string? Build(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Length > MaxLength ? text[..MaxLength] : text;
        var terms = trimmed
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Replace("\"", string.Empty, StringComparison.Ordinal))
            .Where(term => term.Any(char.IsLetterOrDigit))
            .Take(MaxTerms)
            .ToList();
        if (terms.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var term in terms)
        {
            if (builder.Length > 0)
            {
                builder.Append(" AND ");
            }

            builder.Append('"').Append(term).Append("\"*");
        }

        return builder.ToString();
    }
}
