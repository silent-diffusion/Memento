using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Memento.Documents.Tests.Support;

/// <summary>Reads a Word or Excel package's parts, changes one, and zips it again, to craft damaged documents in tests.</summary>
internal static partial class PackageParts
{
    public static List<(string Name, byte[] Data)> Read(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
        var entries = new List<(string Name, byte[] Data)>();
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            entries.Add((entry.FullName, copy.ToArray()));
        }

        return entries;
    }

    public static byte[] Write(IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(data);
            }
        }

        return output.ToArray();
    }

    /// <summary>The package with one part's text replaced by <paramref name="change"/>.</summary>
    public static byte[] WithPart(byte[] package, string part, Func<string, string> change)
    {
        var entries = Read(package);
        var index = entries.FindIndex(e => e.Name == part);
        entries[index] = (part, Encoding.UTF8.GetBytes(change(Encoding.UTF8.GetString(entries[index].Data))));
        return Write(entries);
    }

    /// <summary>The package with <paramref name="depth"/> nested <paramref name="open"/>…<paramref name="close"/> elements just inside the body or sheet data of <paramref name="part"/>.</summary>
    public static byte[] WithNestedPart(byte[] package, string part, string open, string close, int depth) =>
        WithPart(package, part, text =>
        {
            var at = InsertionPoint(text);
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
        });

    /// <summary>Just inside the body, sheet data or root element of a part, where nesting reaches the parser.</summary>
    public static int InsertionPoint(string text)
    {
        var anchor = AnchorPattern().Match(text);
        if (anchor.Success)
        {
            return anchor.Index + anchor.Length;
        }

        var declaration = text.StartsWith("<?xml", StringComparison.Ordinal) ? text.IndexOf("?>", StringComparison.Ordinal) : 0;
        var root = text.IndexOf('<', Math.Max(0, declaration));
        return root < 0 ? 0 : text.IndexOf('>', root) + 1;
    }

    [GeneratedRegex(@"<(?:\w+:)?(?:body|sheetData|sst|styles|numbering|workbook|styleSheet)\b[^>]*>", RegexOptions.None, 5000)]
    private static partial Regex AnchorPattern();
}
