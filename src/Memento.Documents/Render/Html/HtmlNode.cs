namespace Memento.Documents.Render.Html;

/// <summary>An element or text node of the small tree <see cref="HtmlParser"/> builds.</summary>
internal sealed class HtmlNode
{
    private HtmlNode(string? name, string? text)
    {
        Name = name;
        Text = text;
    }

    /// <summary>Lower-case tag name; <c>null</c> for a text node.</summary>
    public string? Name { get; }

    /// <summary>Decoded text of a text node.</summary>
    public string? Text { get; }

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<HtmlNode> Children { get; } = [];

    public HtmlNode? Parent { get; private set; }

    public bool IsText => Name is null;

    public static HtmlNode Element(string name) => new(name, null);

    public static HtmlNode TextNode(string text) => new(null, text);

    public void Add(HtmlNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public string? Attr(string name) => Attributes.GetValueOrDefault(name);

    public bool HasClass(string className)
    {
        var value = Attr("class");
        return value is not null && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className, StringComparer.Ordinal);
    }

    public bool Is(string name) => string.Equals(Name, name, StringComparison.Ordinal);

    public IEnumerable<HtmlNode> Elements() => Children.Where(c => !c.IsText);

    /// <summary>Depth-first search for the first element matching <paramref name="predicate"/> (this node included).</summary>
    public HtmlNode? Find(Func<HtmlNode, bool> predicate)
    {
        // Iterative (an explicit stack, children pushed in reverse) so the search order is the recursive one without its depth.
        var pending = new Stack<HtmlNode>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!node.IsText && predicate(node))
            {
                return node;
            }

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(node.Children[i]);
            }
        }

        return null;
    }

    /// <summary>Every element below this node matching <paramref name="predicate"/>, not descending into matches.</summary>
    public IEnumerable<HtmlNode> FindAll(Func<HtmlNode, bool> predicate)
    {
        var pending = new Stack<HtmlNode>();
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            pending.Push(Children[i]);
        }

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node.IsText)
            {
                continue;
            }

            if (predicate(node))
            {
                yield return node;
                continue;
            }

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(node.Children[i]);
            }
        }
    }

    /// <summary>All text below this node, concatenated as written.</summary>
    public string InnerText()
    {
        if (IsText)
        {
            return Text ?? string.Empty;
        }

        if (Is("br"))
        {
            return "\n";
        }

        return string.Concat(Children.Select(c => c.InnerText()));
    }
}
