using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>
/// The agenda parsers' regular expressions all carry <see cref="TimeoutMilliseconds"/>; a match that runs past it on
/// hostile text counts as no match (the line is then read as plain text) instead of tying up the host.
/// </summary>
internal static class RegexGuard
{
    /// <summary>The match timeout of every <c>[GeneratedRegex]</c> in agenda parsing.</summary>
    public const int TimeoutMilliseconds = 250;

    /// <summary>Lines longer than this are not run through the structural patterns (headings, tables, inline markup).</summary>
    public const int MaxLineLength = 4_000;

    public static bool IsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static Match Match(Regex regex, string input)
    {
        try
        {
            return regex.Match(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return System.Text.RegularExpressions.Match.Empty;
        }
    }

    /// <summary>The matches of <paramref name="regex"/>, read eagerly; none when matching runs past the timeout.</summary>
    public static IReadOnlyList<Match> Matches(Regex regex, string input)
    {
        try
        {
            return regex.Matches(input).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }
}
