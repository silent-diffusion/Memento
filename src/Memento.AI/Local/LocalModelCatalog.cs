using System.Text.Json;

namespace Memento.AI.Local;

/// <summary>The built-in local LLM entries (embedded <c>Memento.AI.Local.local-models.json</c>).</summary>
public static class LocalModelCatalog
{
    public const string ResourceName = "Memento.AI.Local.local-models.json";

    /// <summary>The engine folder under <c>models\</c> and the catalog <c>engine</c> value.</summary>
    public const string Engine = "llama";

    /// <summary>The catalog <c>kind</c> of a local LLM (Core's <c>ModelKinds</c> gains it on integration).</summary>
    public const string Kind = "llm";

    public const string Qwen35FourB = "qwen3.5-4b-q4";
    public const string Ministral3ThreeB = "ministral-3-3b-q4";

    private static readonly Lazy<IReadOnlyList<LocalModelEntry>> Entries = new(Load);

    public static IReadOnlyList<LocalModelEntry> Models => Entries.Value;

    public static LocalModelEntry? Find(string id) => Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    /// <summary>The raw JSON, for merging into Core's model catalog.</summary>
    public static Stream OpenJson() =>
        typeof(LocalModelCatalog).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");

    private static IReadOnlyList<LocalModelEntry> Load()
    {
        using var stream = OpenJson();
        var document = JsonSerializer.Deserialize(stream, LocalLlmJsonContext.Default.LocalModelCatalogDocument)
            ?? throw new InvalidOperationException("The local model catalog is empty.");
        if (document.SchemaVersion != LocalModelCatalogDocument.CurrentSchemaVersion)
        {
            throw new InvalidOperationException("The local model catalog has an unknown schema version.");
        }

        return document.Models;
    }
}
