using System.Net;
using System.Text;

namespace Memento.Documents.Render.Html;

/// <summary>
/// A small, forgiving HTML parser for the viewer's edited markup (what a WebView2 <c>contenteditable</c> produces):
/// elements, attributes, text and entities, void elements, and the implied end tags of <c>p</c>, <c>li</c>, table rows and
/// cells. Comments, doctypes, scripts and styles are dropped. It never throws on malformed input.
/// </summary>
internal static class HtmlParser
{
    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> ClosesParagraph = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "div", "dl", "fieldset", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hr", "main", "nav", "ol", "p", "pre", "section", "table", "ul",
    };

    public static HtmlNode Parse(string html)
    {
        var root = HtmlNode.Element("#root");
        var stack = new List<HtmlNode> { root };
        var text = new StringBuilder();
        var i = 0;
        while (i < html.Length)
        {
            var c = html[i];
            if (c != '<')
            {
                text.Append(c);
                i++;
                continue;
            }

            if (Starts(html, i, "<!--"))
            {
                Flush(text, stack);
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }

            if (Starts(html, i, "<!") || Starts(html, i, "<?"))
            {
                Flush(text, stack);
                var end = html.IndexOf('>', i);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }

            if (i + 1 < html.Length && html[i + 1] == '/')
            {
                var end = html.IndexOf('>', i);
                if (end < 0)
                {
                    text.Append(html, i, html.Length - i);
                    break;
                }

                Flush(text, stack);
                var name = html[(i + 2)..end].Trim().ToLowerInvariant();
                Close(stack, name);
                i = end + 1;
                continue;
            }

            if (i + 1 < html.Length && char.IsAsciiLetter(html[i + 1]))
            {
                Flush(text, stack);
                i = ReadStartTag(html, i, stack);
                continue;
            }

            text.Append(c);
            i++;
        }

        Flush(text, stack);
        return root;
    }

    private static int ReadStartTag(string html, int start, List<HtmlNode> stack)
    {
        var i = start + 1;
        var nameStart = i;
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>' && html[i] != '/')
        {
            i++;
        }

        var element = HtmlNode.Element(html[nameStart..i].ToLowerInvariant());
        var selfClosing = false;
        while (i < html.Length && html[i] != '>')
        {
            if (char.IsWhiteSpace(html[i]))
            {
                i++;
                continue;
            }

            if (html[i] == '/')
            {
                selfClosing = true;
                i++;
                continue;
            }

            var attrStart = i;
            while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '=' && html[i] != '>' && html[i] != '/')
            {
                i++;
            }

            var attrName = html[attrStart..i].ToLowerInvariant();
            var value = string.Empty;
            while (i < html.Length && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            if (i < html.Length && html[i] == '=')
            {
                i++;
                while (i < html.Length && char.IsWhiteSpace(html[i]))
                {
                    i++;
                }

                if (i < html.Length && (html[i] == '"' || html[i] == '\''))
                {
                    var quote = html[i];
                    var close = html.IndexOf(quote, i + 1);
                    if (close < 0)
                    {
                        close = html.Length;
                    }

                    value = html[(i + 1)..close];
                    i = Math.Min(close + 1, html.Length);
                }
                else
                {
                    var valueStart = i;
                    while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>')
                    {
                        i++;
                    }

                    value = html[valueStart..i];
                }
            }

            if (attrName.Length > 0)
            {
                element.Attributes.TryAdd(attrName, WebUtility.HtmlDecode(value));
            }
        }

        i = Math.Min(i + 1, html.Length);
        var name = element.Name!;
        if (name is "script" or "style" or "template")
        {
            var end = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return html.Length;
            }

            var close = html.IndexOf('>', end);
            return close < 0 ? html.Length : close + 1;
        }

        ImplyEndTags(stack, name);
        stack[^1].Add(element);
        if (!selfClosing && !Void.Contains(name))
        {
            stack.Add(element);
        }

        return i;
    }

    private static void ImplyEndTags(List<HtmlNode> stack, string opening)
    {
        if (ClosesParagraph.Contains(opening))
        {
            CloseIfOpenInScope(stack, "p", ["li", "td", "th", "div", "section", "article", "blockquote", "dd", "dt"]);
        }

        switch (opening)
        {
            case "li":
                CloseIfOpenInScope(stack, "li", ["ul", "ol"]);
                break;
            case "dt" or "dd":
                CloseIfOpenInScope(stack, "dt", ["dl"]);
                CloseIfOpenInScope(stack, "dd", ["dl"]);
                break;
            case "tr":
                CloseIfOpenInScope(stack, "tr", ["table", "thead", "tbody", "tfoot"]);
                break;
            case "td" or "th":
                CloseIfOpenInScope(stack, "td", ["tr", "table"]);
                CloseIfOpenInScope(stack, "th", ["tr", "table"]);
                break;
            case "thead" or "tbody" or "tfoot":
                CloseIfOpenInScope(stack, "thead", ["table"]);
                CloseIfOpenInScope(stack, "tbody", ["table"]);
                CloseIfOpenInScope(stack, "tfoot", ["table"]);
                break;
        }
    }

    /// <summary>Closes the nearest open <paramref name="name"/> unless one of <paramref name="boundaries"/> is open above it.</summary>
    private static void CloseIfOpenInScope(List<HtmlNode> stack, string name, string[] boundaries)
    {
        for (var k = stack.Count - 1; k > 0; k--)
        {
            if (stack[k].Is(name))
            {
                stack.RemoveRange(k, stack.Count - k);
                return;
            }

            if (boundaries.Contains(stack[k].Name, StringComparer.Ordinal))
            {
                return;
            }
        }
    }

    private static void Close(List<HtmlNode> stack, string name)
    {
        for (var k = stack.Count - 1; k > 0; k--)
        {
            if (stack[k].Is(name))
            {
                stack.RemoveRange(k, stack.Count - k);
                return;
            }
        }

        // A stray end tag (</p> with no open p) is ignored.
    }

    private static void Flush(StringBuilder text, List<HtmlNode> stack)
    {
        if (text.Length == 0)
        {
            return;
        }

        stack[^1].Add(HtmlNode.TextNode(WebUtility.HtmlDecode(text.ToString())));
        text.Clear();
    }

    private static bool Starts(string html, int index, string value) =>
        string.CompareOrdinal(html, index, value, 0, value.Length) == 0;
}
