using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>Strips Markdown inline markup (emphasis, code, links, images, HTML tags) down to the words.</summary>
internal static partial class MarkdownInline
{
    public static string Strip(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

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

    [GeneratedRegex(@"<!--.*?-->")]
    private static partial Regex HtmlCommentPattern();

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\([^)]*\)")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\](?:\([^)]*\)|\[[^\]]*\])")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<(?<url>https?://[^>\s]+)>")]
    private static partial Regex AutoLinkPattern();

    [GeneratedRegex(@"`+(?<code>[^`]+)`+")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"(\*\*|__)(?<inner>\S(?:.*?\S)?)\1")]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"(?<![\w*])\*(?<inner>\S(?:.*?\S)?)\*(?![\w*])")]
    private static partial Regex ItalicStarPattern();

    [GeneratedRegex(@"(?<![\w_])_(?<inner>\S(?:.*?\S)?)_(?![\w_])")]
    private static partial Regex ItalicUnderscorePattern();

    [GeneratedRegex(@"~~(?<inner>.+?)~~")]
    private static partial Regex StrikePattern();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTagPattern();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\\(?<c>[\\`*_{}\[\]()#+\-.!|>])")]
    private static partial Regex EscapePattern();
}
