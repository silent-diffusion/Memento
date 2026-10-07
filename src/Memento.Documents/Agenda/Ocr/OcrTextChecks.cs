using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// Spots words text recognition probably misread: stray symbols, and digits mixed with letters that look like
/// digits ("S45,DDO" for "$45,000", "2O31" for "2031"). Ordinary mixes such as "Q3", "B2B" or "10am" pass.
/// </summary>
internal static partial class OcrTextChecks
{
    private const string Confusable = "ODSIlBZGQoqsgz";

    public static string? FindSuspicious(string text)
    {
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim('.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'', '“', '”', '‘', '’');
            if (token.Length == 0)
            {
                continue;
            }

            if (token.IndexOfAny(['|', '¦', '§', '¢', '©', '®', '~', '{', '}', '\\', '^', '`', '¬', '�', '¤', '¥']) >= 0)
            {
                return raw;
            }

            var digits = token.Count(char.IsDigit);
            var letters = token.Count(char.IsLetter);
            if (digits == 0 || letters == 0)
            {
                continue;
            }

            if (letters >= 2 && digits >= 2 && token.Where(char.IsLetter).All(c => Confusable.Contains(c, StringComparison.Ordinal)))
            {
                return raw;
            }

            if (SandwichPattern().IsMatch(token) || MisreadTimePattern().IsMatch(token))
            {
                return raw;
            }
        }

        return null;
    }

    /// <summary>A confusable letter between digits: "2O31", "1l0".</summary>
    [GeneratedRegex(@"\d[OoIlSB]\d")]
    private static partial Regex SandwichPattern();

    /// <summary>A time with a letter for a digit: "1O:30", "10:3O".</summary>
    [GeneratedRegex(@"^(?=.*[OoIlS])[\dOoIlS]{1,2}[:.][\dOoIlS]{2}$")]
    private static partial Regex MisreadTimePattern();
}
