using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>Strips Markdown inline markup (emphasis, code, links, images, HTML tags) down to the words.</summary>
internal static partial class MarkdownInline
{
    /// <summary>Text with none of these has no inline markup, so most lines skip the patterns entirely.</summary>
    private static readonly System.Buffers.SearchValues<char> MarkupCharacters = System.Buffers.SearchValues.Create("<[`*_~\\&");

    public static string Strip(string text)
    {
        // A line longer than any agenda item is left as written (it is marked as too long anyway); every pattern's
        // repetition is bounded too, so a run of "*a " or "[a" costs a fixed amount per character, not the whole line.
        if (text.Length == 0 || text.Length > RegexGuard.MaxLineLength || text.AsSpan().IndexOfAny(MarkupCharacters) < 0)
        {
            return text;
        }

        try
        {
            var result = HtmlCommentPattern().Replace(text, string.Empty);
            result = ImagePattern().Replace(result, "${alt}");
            result = LinkPattern().Replace(result, "${text}");
            result = AutoLinkPattern().Replace(result, "${url}");
            result = CodePattern().Replace(result, "${code}");
            result = BoldPattern().Replace(result, "${inner}");
            result = ItalicStarPattern().Replace(result, "${inner}");
            result = ItalicUnderscorePattern().Replace(result, "${inner}");
            result = StrikePattern().Replace(result, "${inner}");
            result = BreakTagPattern().Replace(result, " ");
            result = HtmlTagPattern().Replace(result, string.Empty);
            result = EscapePattern().Replace(result, "${c}");
            return result.Replace("&amp;", "&", StringComparison.Ordinal).Replace("&nbsp;", " ", StringComparison.Ordinal);
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }

    [GeneratedRegex(@"<!--.{0,1000}?-->", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex HtmlCommentPattern();

    [GeneratedRegex(@"!\[(?<alt>[^\]]{0,500})\]\([^)]{0,1000}\)", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[(?<text>[^\]]{1,500})\](?:\([^)]{0,1000}\)|\[[^\]]{0,500}\])", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<(?<url>https?://[^>\s]{1,1000})>", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex AutoLinkPattern();

    [GeneratedRegex(@"(?<!`)`+(?<code>[^`]{1,500})`+", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"(\*\*|__)(?<inner>\S(?:.{0,200}?\S)?)\1", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"(?<![\w*])\*(?<inner>\S(?:.{0,200}?\S)?)\*(?![\w*])", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex ItalicStarPattern();

    [GeneratedRegex(@"(?<![\w_])_(?<inner>\S(?:.{0,200}?\S)?)_(?![\w_])", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex ItalicUnderscorePattern();

    [GeneratedRegex(@"~~(?<inner>.{1,200}?)~~", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex StrikePattern();

    [GeneratedRegex(@"<br\s{0,20}/?>", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex BreakTagPattern();

    [GeneratedRegex(@"</?[a-zA-Z][^>]{0,500}>", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\\(?<c>[\\`*_{}\[\]()#+\-.!|>])", RegexOptions.None, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex EscapePattern();
}
