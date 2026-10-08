using System.Text;
using System.Text.RegularExpressions;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Fuzz;

/// <summary>
/// Structure-aware mutations of a Word or Excel package: unzips it, changes one XML part (attribute values such as
/// <c>w:gridSpan="x"</c>, <c>r="ZZZZZZ1"</c> or <c>row r="4294967295"</c>, deep nesting, a DTD, duplicated elements,
/// byte damage, truncation, a missing part) and zips it again, so the parser gets past the ZIP and into the XML.
/// </summary>
internal static partial class PackageMutator
{
    private static readonly string[] MainParts =
    [
        "word/document.xml", "word/styles.xml", "word/numbering.xml", "xl/workbook.xml", "xl/sharedStrings.xml", "xl/styles.xml",
        "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml",
    ];

    private static readonly string[] Values =
    [
        "x", string.Empty, "-1", "0", "1.5", "4294967295", "4294967296", "2147483648", "99999999999999999999", "ZZZZZZ1", "A0",
        "XFE1048577", "1e308", "NaN", "restart", "continue", "page", "999999", "rId999", "s", "inlineStr", "b", "e", "hidden",
    ];

    /// <summary>Targeted changes: attributes the parsers read with typed getters or as indexes.</summary>
    private static readonly (string Pattern, string Replacement)[] Targeted =
    [
        (@"<w:gridSpan w:val=""[^""]*""", @"<w:gridSpan w:val=""x"""),
        (@"<w:gridSpan w:val=""[^""]*""", @"<w:gridSpan w:val=""1000000"""),
        (@"<w:tcPr>", @"<w:tcPr><w:gridSpan w:val=""2147483647""/>"),
        (@"<w:tcPr>", @"<w:tcPr><w:vMerge w:val=""bogus""/>"),
        (@"<w:r>", @"<w:r><w:br w:type=""sideways""/>"),
        (@"<w:numId w:val=""[^""]*""", @"<w:numId w:val=""x"""),
        (@"<w:ilvl w:val=""[^""]*""", @"<w:ilvl w:val=""-5"""),
        (@"<w:outlineLvl w:val=""[^""]*""", @"<w:outlineLvl w:val=""q"""),
        (@"<w:start w:val=""[^""]*""", @"<w:start w:val=""2147483647"""),
        (@"<w:pStyle w:val=""[^""]*""", @"<w:pStyle w:val=""Heading9999"""),
        (@"<(?<p>\w+:)?c r=""[A-Z]+", @"<${p}c r=""ZZZZZZ"),
        (@"<(?<p>\w+:)?c r=""[A-Z]+[0-9]+""", @"<${p}c r=""XFE1"""),
        (@"<(?<p>\w+:)?row r=""[0-9]+""", @"<${p}row r=""4294967295"""),
        (@"<(?<p>\w+:)?row r=""[0-9]+""", @"<${p}row r=""2147483648"""),
        (@"<(?<p>\w+:)?row r=""[0-9]+""", @"<${p}row r=""0"""),
        (@"<(?<p>\w+:)?row r=""[0-9]+""", @"<${p}row r=""x"""),
        (@" s=""[0-9]+""", @" s=""4294967295"""),
        (@" s=""[0-9]+""", @" s=""x"""),
        (@" t=""s""", @" t=""zz"""),
        (@"<(?<p>\w+:)?v>[^<]*</", @"<${p}v>99999999999</"),
        (@"<(?<p>\w+:)?v>[^<]*</", @"<${p}v>1e308</"),
        (@"<(?<p>\w+:)?sheet ", @"<${p}sheet state=""bogus"" "),
        (@"r:id=""[^""]*""", @"r:id=""rId999"""),
        (@"<(?<p>\w+:)?mergeCell ref=""[^""]*""", @"<${p}mergeCell ref=""A1:ZZZZZZ1"""),
        (@"<(?<p>\w+:)?numFmt numFmtId=""[^""]*""", @"<${p}numFmt numFmtId=""x"""),
        (@"<(?<p>\w+:)?xf numFmtId=""[^""]*""", @"<${p}xf numFmtId=""4294967295"""),
    ];

    public static (byte[] Bytes, string Description) Mutate(byte[] package, Random random)
    {
        var entries = PackageParts.Read(package);
        var xml = entries.Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)).ToList();
        var main = xml.Where(e => MainParts.Contains(e.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var pool = main.Count > 0 && random.Next(10) < 7 ? main : xml;
        var target = pool[random.Next(pool.Count)];
        var index = entries.FindIndex(e => e.Name == target.Name);
        var text = Encoding.UTF8.GetString(target.Data);
        string description;
        switch (random.Next(10))
        {
            case 0:
            {
                var attributes = AttributePattern().Matches(text);
                if (attributes.Count == 0)
                {
                    goto default;
                }

                var attribute = attributes[random.Next(attributes.Count)];
                var value = Values[random.Next(Values.Length)];
                var group = attribute.Groups["v"];
                text = string.Concat(text.AsSpan(0, group.Index), value, text.AsSpan(group.Index + group.Length));
                description = $"{target.Name}: {attribute.Groups["n"].Value}=\"{value}\"";
                break;
            }

            case 1:
            case 2:
            {
                var start = random.Next(Targeted.Length);
                description = $"{target.Name}: no targeted attribute";
                for (var k = 0; k < Targeted.Length; k++)
                {
                    var (pattern, replacement) = Targeted[(start + k) % Targeted.Length];
                    var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                    var matches = regex.Matches(text);
                    if (matches.Count > 0)
                    {
                        var match = matches[random.Next(matches.Count)];
                        text = string.Concat(text.AsSpan(0, match.Index), match.Result(replacement), text.AsSpan(match.Index + match.Length));
                        description = $"{target.Name}: {replacement}";
                        break;
                    }
                }

                break;
            }

            case 3:
            {
                var depth = random.Next(2) == 0 ? 300 + random.Next(700) : 5_000 + random.Next(20_000);
                text = Nest(text, depth, random);
                description = $"{target.Name}: nested {depth} deep";
                break;
            }

            case 4:
            {
                var declaration = text.StartsWith("<?xml", StringComparison.Ordinal) ? text.IndexOf("?>", StringComparison.Ordinal) + 2 : 0;
                const string Dtd = "<!DOCTYPE r [<!ENTITY a \"aaaaaaaaaaaaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">]>";
                text = string.Concat(text.AsSpan(0, declaration), Dtd, text.AsSpan(declaration));
                description = $"{target.Name}: DTD with entities";
                break;
            }

            case 5:
            {
                var elements = ElementPattern().Matches(text);
                if (elements.Count == 0)
                {
                    goto default;
                }

                var element = elements[random.Next(elements.Count)];
                var times = 1 + random.Next(random.Next(2) == 0 ? 10 : 2_000);
                var copies = new StringBuilder(element.Length * times);
                for (var t = 0; t < times; t++)
                {
                    copies.Append(element.Value);
                }

                text = string.Concat(text.AsSpan(0, element.Index + element.Length), copies.ToString(), text.AsSpan(element.Index + element.Length));
                description = $"{target.Name}: <{element.Groups["n"].Value}> repeated {times}x";
                break;
            }

            case 6:
            {
                var length = random.Next(text.Length);
                text = text[..length];
                description = $"{target.Name}: XML truncated to {length}";
                break;
            }

            case 7:
                entries.RemoveAt(index);
                return (PackageParts.Write(entries), $"{target.Name} removed");

            default:
            {
                var (mutated, step) = ByteMutator.MutateOnce(target.Data, random);
                entries[index] = (target.Name, mutated);
                return (PackageParts.Write(entries), $"{target.Name}: {step}");
            }
        }

        entries[index] = (target.Name, Encoding.UTF8.GetBytes(text));
        return (PackageParts.Write(entries), description);
    }

    private static string Nest(string text, int depth, Random random)
    {
        var word = text.Contains("<w:body>", StringComparison.Ordinal);
        var (open, close) = (word, random.Next(3)) switch
        {
            (true, 0) => ("<w:sdt><w:sdtContent>", "</w:sdtContent></w:sdt>"),
            (true, 1) => ("<w:customXml w:element=\"x\">", "</w:customXml>"),
            (true, _) => ("<w:tbl><w:tr><w:tc>", "</w:tc></w:tr></w:tbl>"),
            (false, 0) => ("<x:a xmlns:x=\"urn:x\">", "</x:a>"),
            _ => ("<is><r><t>", "</t></r></is>"),
        };
        var at = PackageParts.InsertionPoint(text);
        var nested = new StringBuilder((open.Length + close.Length) * depth);
        for (var d = 0; d < depth; d++)
        {
            nested.Append(open);
        }

        for (var d = 0; d < depth; d++)
        {
            nested.Append(close);
        }

        return string.Concat(text.AsSpan(0, at), nested.ToString(), text.AsSpan(at));
    }

    [GeneratedRegex(@"(?<n>[\w:]+)=""(?<v>[^""]*)""", RegexOptions.None, 5000)]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"<(?<n>(?:\w+:)?(?:p|tr|tc|row|c|si|style|abstractNum|num|sheet|mergeCell))\b[^>]*?(?:/>|>.*?</\k<n>>)", RegexOptions.Singleline, 5000)]
    private static partial Regex ElementPattern();
}
