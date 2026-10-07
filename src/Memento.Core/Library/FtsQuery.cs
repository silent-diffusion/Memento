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
    public static string? Build(string? text) => Join(Terms(text), " AND ");

    /// <summary>
    /// <c>{column} : ("a"* OR "b"*)</c>: rows where <paramref name="column"/> holds any of the words (used to find the
    /// transcript snippet of a row that matched), or <c>null</c> when nothing searchable was typed.
    /// </summary>
    public static string? BuildColumnAny(string? text, string column) =>
        Join(Terms(text), " OR ") is { } any ? $"{{{column}}} : ({any})" : null;

    private static List<string> Terms(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var trimmed = text.Length > MaxLength ? text[..MaxLength] : text;
        return trimmed
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Replace("\"", string.Empty, StringComparison.Ordinal))
            .Where(term => term.Any(char.IsLetterOrDigit))
            .Take(MaxTerms)
            .ToList();
    }

    private static string? Join(List<string> terms, string separator)
    {
        if (terms.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var term in terms)
        {
            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append('"').Append(term).Append("\"*");
        }

        return builder.ToString();
    }
}
