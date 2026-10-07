using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Memento.AI.Local;

/// <summary>
/// Turns the JSON Schema of an <see cref="AiRequest"/> into a GBNF grammar for llama.cpp, so the pipeline writes one
/// schema for every provider. Whitespace between tokens is allowed the way people and models write JSON (a space, or
/// a line break and indentation): a grammar that forbids line breaks cut the 3B model's recall from 0.93 to 0.57
/// (ENGINE-NOTES.md section H, trap 4). Supported: <c>type</c> (including type lists), <c>properties</c> (all
/// emitted, in declaration order, as strict structured outputs require), <c>items</c>, <c>minItems</c>,
/// <c>maxItems</c>, <c>enum</c>, <c>const</c>, <c>anyOf</c>/<c>oneOf</c>, <c>maxLength</c>. Not supported:
/// <c>$ref</c>, <c>patternProperties</c>, <c>pattern</c> (ignored).
/// </summary>
public static class JsonSchemaGrammar
{
    /// <summary>The rules every generated grammar ends with.</summary>
    public const string CommonRules = """
        ws ::= | " " | "\n" [ \t]{0,24}
        string ::= "\"" char* "\""
        char ::= [^"\\\x7F\x00-\x1F] | "\\" (["\\/bfnrt] | "u" [0-9a-fA-F]{4})
        number ::= "-"? ("0" | [1-9] [0-9]{0,15}) ("." [0-9]{1,8})? ([eE] [-+]? [0-9]{1,3})?
        integer ::= "-"? ("0" | [1-9] [0-9]{0,15})
        boolean ::= "true" | "false"
        null ::= "null"
        value ::= object | array | string | number | boolean | null
        object ::= "{" ws ( string ws ":" ws value ( ws "," ws string ws ":" ws value )* )? ws "}"
        array ::= "[" ws ( value ( ws "," ws value )* )? ws "]"
        """;

    /// <summary>A grammar whose <c>root</c> rule accepts exactly the JSON documents <paramref name="schema"/> describes.</summary>
    /// <exception cref="NotSupportedException">The schema uses <c>$ref</c> or another unsupported construct.</exception>
    public static string FromSchema(JsonElement schema)
    {
        var rules = new List<(string Name, string Body)>();
        var names = new HashSet<string>(StringComparer.Ordinal) { "root", "ws", "string", "char", "number", "integer", "boolean", "null", "value", "object", "array" };
        var root = Expression(schema, "r", rules, names);
        var builder = new StringBuilder();
        builder.Append("root ::= ").Append(root).Append('\n');
        foreach (var (name, body) in rules)
        {
            builder.Append(name).Append(" ::= ").Append(body).Append('\n');
        }

        builder.Append(CommonRules.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\n');
        return builder.ToString();
    }

    private static string Expression(JsonElement schema, string name, List<(string Name, string Body)> rules, HashSet<string> names)
    {
        if (schema.ValueKind == JsonValueKind.True || (schema.ValueKind == JsonValueKind.Object && !schema.EnumerateObject().Any()))
        {
            return "value";
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new NotSupportedException("A JSON schema must be an object or true.");
        }

        if (schema.TryGetProperty("$ref", out _))
        {
            throw new NotSupportedException("JSON schema references ($ref) are not supported for local grammars; inline the definition.");
        }

        if (schema.TryGetProperty("const", out var constant))
        {
            return Literal(constant.GetRawText());
        }

        if (schema.TryGetProperty("enum", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            return "(" + string.Join(" | ", choices.EnumerateArray().Select(c => Literal(c.GetRawText()))) + ")";
        }

        foreach (var keyword in new[] { "anyOf", "oneOf" })
        {
            if (schema.TryGetProperty(keyword, out var members) && members.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                return "(" + string.Join(" | ", members.EnumerateArray().Select(m => Expression(m, name + "-" + index++.ToString(CultureInfo.InvariantCulture), rules, names))) + ")";
            }
        }

        if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array)
        {
            return "(" + string.Join(" | ", type.EnumerateArray().Select(t => TypeExpression(t.GetString() ?? "null", schema, name, rules, names))) + ")";
        }

        var single = type.ValueKind == JsonValueKind.String ? type.GetString()! : schema.TryGetProperty("properties", out _) ? "object" : schema.TryGetProperty("items", out _) ? "array" : null;
        return single is null ? "value" : TypeExpression(single, schema, name, rules, names);
    }

    private static string TypeExpression(string type, JsonElement schema, string name, List<(string Name, string Body)> rules, HashSet<string> names) => type switch
    {
        "object" => ObjectRule(schema, name, rules, names),
        "array" => ArrayRule(schema, name, rules, names),
        "string" when schema.TryGetProperty("maxLength", out var max) && max.TryGetInt32(out var length) =>
            "\"\\\"\" char{0," + length.ToString(CultureInfo.InvariantCulture) + "} \"\\\"\"",
        "string" => "string",
        "number" => "number",
        "integer" => "integer",
        "boolean" => "boolean",
        "null" => "null",
        _ => throw new NotSupportedException($"JSON schema type '{type}' is not supported."),
    };

    private static string ObjectRule(JsonElement schema, string name, List<(string Name, string Body)> rules, HashSet<string> names)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object || !properties.EnumerateObject().Any())
        {
            return "object";
        }

        var rule = Unique(name, names);
        var parts = new List<string>();
        foreach (var property in properties.EnumerateObject())
        {
            var value = Expression(property.Value, rule + "-" + Slug(property.Name), rules, names);
            parts.Add(Literal(JsonSerializer.Serialize(property.Name)) + " ws \":\" ws " + value);
        }

        rules.Add((rule, "\"{\" ws " + string.Join(" ws \",\" ws ", parts) + " ws \"}\""));
        return rule;
    }

    private static string ArrayRule(JsonElement schema, string name, List<(string Name, string Body)> rules, HashSet<string> names)
    {
        var rule = Unique(name, names);
        var item = schema.TryGetProperty("items", out var items) ? Expression(items, rule + "-item", rules, names) : "value";
        var min = schema.TryGetProperty("minItems", out var minItems) && minItems.TryGetInt32(out var m) ? Math.Max(0, m) : 0;
        int? max = schema.TryGetProperty("maxItems", out var maxItems) && maxItems.TryGetInt32(out var x) ? Math.Max(0, x) : null;
        var more = "(ws \",\" ws " + item + ")";
        string body;
        if (max == 0)
        {
            body = "\"[\" ws \"]\"";
        }
        else
        {
            var repeat = (min > 1 ? min - 1 : 0, max is { } upper ? upper - 1 : (int?)null) switch
            {
                (0, null) => more + "*",
                (var lo, null) => more + "{" + lo.ToString(CultureInfo.InvariantCulture) + ",}",
                (var lo, int hi) => more + "{" + lo.ToString(CultureInfo.InvariantCulture) + "," + hi.ToString(CultureInfo.InvariantCulture) + "}",
            };
            var list = item + " " + repeat;
            body = "\"[\" ws " + (min == 0 ? "(" + list + ")?" : list) + " ws \"]\"";
        }

        rules.Add((rule, body));
        return rule;
    }

    private static string Unique(string name, HashSet<string> names)
    {
        var candidate = name;
        for (var i = 2; !names.Add(candidate); i++)
        {
            candidate = name + "-" + i.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "p" : slug;
    }

    /// <summary>A GBNF string literal matching <paramref name="text"/> exactly.</summary>
    internal static string Literal(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        builder.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
