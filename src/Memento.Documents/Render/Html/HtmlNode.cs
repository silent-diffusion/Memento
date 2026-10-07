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
        if (!IsText && predicate(this))
        {
            return this;
        }

        foreach (var child in Children)
        {
            var found = child.Find(predicate);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Every element below this node matching <paramref name="predicate"/>, not descending into matches.</summary>
    public IEnumerable<HtmlNode> FindAll(Func<HtmlNode, bool> predicate)
    {
        foreach (var child in Children)
        {
            if (child.IsText)
            {
                continue;
            }

            if (predicate(child))
            {
                yield return child;
                continue;
            }

            foreach (var nested in child.FindAll(predicate))
            {
                yield return nested;
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
