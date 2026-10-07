using System.Text.Json;
using Memento.Documents.Model;

namespace Memento.Documents.Styling;

/// <summary>The three presets that ship with Memento (DESIGN.md §13), read from the JSON resources in <c>Styles/BuiltIn</c>.</summary>
public static class BuiltInStyles
{
    public const string CorporateId = "corporate";
    public const string MinimalId = "minimal";
    public const string AcademicId = "academic";

    private static readonly Lazy<IReadOnlyList<DocumentStyle>> Loaded = new(Load);

    public static IReadOnlyList<string> Ids { get; } = [CorporateId, MinimalId, AcademicId];

    public static IReadOnlyList<DocumentStyle> All => Loaded.Value;

    public static DocumentStyle Corporate => Get(CorporateId);

    public static DocumentStyle Minimal => Get(MinimalId);

    public static DocumentStyle Academic => Get(AcademicId);

    public static DocumentStyle Get(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"\"{id}\" is not a built-in style.");

    private static List<DocumentStyle> Load()
    {
        var assembly = typeof(BuiltInStyles).Assembly;
        var result = new List<DocumentStyle>(Ids.Count);
        foreach (var id in Ids)
        {
            var name = $"Memento.Documents.Styles.{id}.json";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"The built-in style resource {name} is missing from the build.");
            var style = JsonSerializer.Deserialize(stream, StyleJsonContext.Default.DocumentStyle)
                ?? throw new DocumentFormatException($"The built-in style resource {name} is empty.");
            result.Add(style with { BuiltIn = true });
        }

        return result;
    }
}
