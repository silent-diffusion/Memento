using System.Globalization;
using System.Text;

namespace Memento.AI.Tests.Fakes;

/// <summary>
/// A small recogniser for the GBNF subset llama.cpp accepts (literals, character classes with ranges and negation,
/// groups, alternation, rule references, <c>* + ?</c> and <c>{m}</c>, <c>{m,}</c>, <c>{m,n}</c>), so grammars can be
/// tested without a model: <see cref="Accepts"/> says whether the whole text matches <c>root</c>. Parsing also
/// proves every referenced rule exists.
/// </summary>
internal sealed class GbnfMatcher
{
    private readonly Dictionary<string, Node> _rules;
    private readonly Dictionary<(string, int), HashSet<int>> _memo = [];
    private string _input = string.Empty;

    private GbnfMatcher(Dictionary<string, Node> rules) => _rules = rules;

    public static GbnfMatcher Parse(string grammar)
    {
        var bodies = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        string? current = null;
        foreach (var raw in grammar.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var define = line.IndexOf("::=", StringComparison.Ordinal);
            if (define > 0 && IsName(line[..define].Trim()))
            {
                current = line[..define].Trim();
                if (bodies.ContainsKey(current))
                {
                    throw new FormatException($"Rule {current} is defined twice.");
                }

                bodies[current] = new StringBuilder(line[(define + 3)..]);
            }
            else if (current is not null)
            {
                bodies[current].Append(' ').Append(line);
            }
            else
            {
                throw new FormatException("Text before the first rule: " + line);
            }
        }

        var rules = bodies.ToDictionary(b => b.Key, b => new Parser(b.Value.ToString()).ParseAll(), StringComparer.Ordinal);
        foreach (var reference in rules.Values.SelectMany(References))
        {
            if (!rules.ContainsKey(reference))
            {
                throw new FormatException($"Rule {reference} is used but not defined.");
            }
        }

        if (!rules.ContainsKey("root"))
        {
            throw new FormatException("No root rule.");
        }

        return new GbnfMatcher(rules);
    }

    public bool Accepts(string text)
    {
        _input = text;
        _memo.Clear();
        return Match(new Ref("root"), 0).Contains(text.Length);
    }

    private static bool IsName(string name) => name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static IEnumerable<string> References(Node node) => node switch
    {
        Ref r => [r.Name],
        Seq s => s.Items.SelectMany(References),
        Alt a => a.Options.SelectMany(References),
        Repeat r => References(r.Item),
        _ => [],
    };

    private HashSet<int> Match(Node node, int position)
    {
        switch (node)
        {
            case Literal literal:
                return string.CompareOrdinal(_input, position, literal.Text, 0, literal.Text.Length) == 0 && position + literal.Text.Length <= _input.Length
                    ? [position + literal.Text.Length]
                    : [];
            case CharClass cls:
                return position < _input.Length && cls.Matches(_input[position]) ? [position + 1] : [];
            case Ref reference:
                if (_memo.TryGetValue((reference.Name, position), out var known))
                {
                    return known;
                }

                _memo[(reference.Name, position)] = [];
                var ends = Match(_rules[reference.Name], position);
                _memo[(reference.Name, position)] = ends;
                return ends;
            case Seq sequence:
                var positions = new HashSet<int> { position };
                foreach (var item in sequence.Items)
                {
                    var next = new HashSet<int>();
                    foreach (var p in positions)
                    {
                        next.UnionWith(Match(item, p));
                    }

                    positions = next;
                    if (positions.Count == 0)
                    {
                        break;
                    }
                }

                return positions;
            case Alt alternation:
                var all = new HashSet<int>();
                foreach (var option in alternation.Options)
                {
                    all.UnionWith(Match(option, position));
                }

                return all;
            case Repeat repeat:
                var results = new HashSet<int>();
                if (repeat.Min == 0)
                {
                    results.Add(position);
                }

                var frontier = new HashSet<int> { position };
                var seen = new HashSet<int> { position };
                for (var count = 1; repeat.Max is null || count <= repeat.Max; count++)
                {
                    var next = new HashSet<int>();
                    foreach (var p in frontier)
                    {
                        foreach (var end in Match(repeat.Item, p))
                        {
                            if (end > p || count <= repeat.Min)
                            {
                                next.Add(end);
                            }
                        }
                    }

                    if (next.Count == 0)
                    {
                        break;
                    }

                    if (count >= repeat.Min)
                    {
                        results.UnionWith(next);
                    }

                    frontier = count < repeat.Min ? next : [.. next.Where(seen.Add)];
                    if (frontier.Count == 0)
                    {
                        break;
                    }
                }

                return results;
            default:
                throw new InvalidOperationException("Unknown node.");
        }
    }

    private abstract record Node;

    private sealed record Literal(string Text) : Node;

    private sealed record CharClass(bool Negated, IReadOnlyList<(char From, char To)> Ranges) : Node
    {
        public bool Matches(char c) => Ranges.Any(r => c >= r.From && c <= r.To) != Negated;
    }

    private sealed record Ref(string Name) : Node;

    private sealed record Seq(IReadOnlyList<Node> Items) : Node;

    private sealed record Alt(IReadOnlyList<Node> Options) : Node;

    private sealed record Repeat(Node Item, int Min, int? Max) : Node;

    private sealed class Parser(string text)
    {
        private int _at;

        public Node ParseAll()
        {
            var node = Alternatives();
            Skip();
            if (_at != text.Length)
            {
                throw new FormatException($"Unexpected '{text[_at]}' at {_at} in: {text}");
            }

            return node;
        }

        private Node Alternatives()
        {
            var options = new List<Node> { Sequence() };
            while (Peek() == '|')
            {
                _at++;
                options.Add(Sequence());
            }

            return options.Count == 1 ? options[0] : new Alt(options);
        }

        private Node Sequence()
        {
            var items = new List<Node>();
            while (true)
            {
                var c = Peek();
                if (c is null or '|' or ')')
                {
                    break;
                }

                var item = Item();
                items.Add(Postfix(item));
            }

            return items.Count == 1 ? items[0] : new Seq(items);
        }

        private Node Item()
        {
            var c = Peek()!.Value;
            switch (c)
            {
                case '"':
                    _at++;
                    var literal = new StringBuilder();
                    while (text[_at] != '"')
                    {
                        literal.Append(text[_at] == '\\' ? Escape() : text[_at++]);
                    }

                    _at++;
                    return new Literal(literal.ToString());
                case '[':
                    _at++;
                    var negated = text[_at] == '^';
                    if (negated)
                    {
                        _at++;
                    }

                    var ranges = new List<(char, char)>();
                    while (text[_at] != ']')
                    {
                        var from = text[_at] == '\\' ? Escape() : text[_at++];
                        var to = from;
                        if (text[_at] == '-' && text[_at + 1] != ']')
                        {
                            _at++;
                            to = text[_at] == '\\' ? Escape() : text[_at++];
                        }

                        ranges.Add((from, to));
                    }

                    _at++;
                    return new CharClass(negated, ranges);
                case '(':
                    _at++;
                    var inner = Alternatives();
                    if (Peek() != ')')
                    {
                        throw new FormatException("Missing ) in: " + text);
                    }

                    _at++;
                    return inner;
                case '.':
                    _at++;
                    return new CharClass(true, []);
                default:
                    var start = _at;
                    while (_at < text.Length && (char.IsAsciiLetterOrDigit(text[_at]) || text[_at] == '-'))
                    {
                        _at++;
                    }

                    if (_at == start)
                    {
                        throw new FormatException($"Unexpected '{c}' at {_at} in: {text}");
                    }

                    return new Ref(text[start.._at]);
            }
        }

        private Node Postfix(Node item)
        {
            if (_at >= text.Length)
            {
                return item;
            }

            switch (text[_at])
            {
                case '*':
                    _at++;
                    return new Repeat(item, 0, null);
                case '+':
                    _at++;
                    return new Repeat(item, 1, null);
                case '?':
                    _at++;
                    return new Repeat(item, 0, 1);
                case '{':
                    var close = text.IndexOf('}', _at);
                    var spec = text[(_at + 1)..close].Split(',');
                    _at = close + 1;
                    var min = int.Parse(spec[0], CultureInfo.InvariantCulture);
                    int? max = spec.Length == 1 ? min : spec[1].Length == 0 ? null : int.Parse(spec[1], CultureInfo.InvariantCulture);
                    return new Repeat(item, min, max);
                default:
                    return item;
            }
        }

        private char Escape()
        {
            _at++;
            var c = text[_at++];
            switch (c)
            {
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'x':
                    var hex = text.Substring(_at, 2);
                    _at += 2;
                    return (char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                default:
                    return c;
            }
        }

        private char? Peek()
        {
            Skip();
            return _at < text.Length ? text[_at] : null;
        }

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at]))
            {
                _at++;
            }
        }
    }
}
