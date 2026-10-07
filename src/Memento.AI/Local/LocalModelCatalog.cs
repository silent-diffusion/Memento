using System.Text.Json;
using Memento.Core.Models;

namespace Memento.AI.Local;

/// <summary>
/// The local LLMs: the <c>kind: "llm"</c> entries of Core's model catalog (<c>catalog.json</c>), each with the run
/// profile from its <c>llm</c> block. The model manager downloads, verifies and removes them like every other model.
/// </summary>
public static class LocalModelCatalog
{
    /// <summary>The engine folder under <c>models\</c> and the catalog <c>engine</c> value.</summary>
    public const string Engine = "llama";

    /// <summary>The catalog <c>kind</c> of a local LLM (<see cref="ModelKinds.Llm"/>).</summary>
    public const string Kind = ModelKinds.Llm;

    public const string Qwen35FourB = "qwen3.5-4b-q4";
    public const string Ministral3ThreeB = "ministral-3-3b-q4";

    private static readonly Lazy<IReadOnlyList<LocalModelEntry>> Entries = new(() => From(ModelCatalog.Default));

    public static IReadOnlyList<LocalModelEntry> Models => Entries.Value;

    public static LocalModelEntry? Find(string id) => Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    /// <summary>The local LLM entries of <paramref name="catalog"/> (entries without a readable <c>llm</c> block are skipped).</summary>
    public static IReadOnlyList<LocalModelEntry> From(ModelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.OfKind(ModelKinds.Llm).Select(ToLocal).OfType<LocalModelEntry>().ToList();
    }

    /// <summary>A Core catalog entry as a local model entry, or <c>null</c> when it has no valid <c>llm</c> block.</summary>
    public static LocalModelEntry? ToLocal(ModelCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != ModelKinds.Llm || entry.ExtensionData is null || !entry.ExtensionData.TryGetValue("llm", out var block))
        {
            return null;
        }

        LocalModelProfile? profile;
        try
        {
            profile = block.Deserialize(LocalLlmJsonContext.Default.LocalModelProfile);
        }
        catch (JsonException)
        {
            return null;
        }

        if (profile is null || !LocalChatTemplates.IsKnown(profile.TemplateId))
        {
            return null;
        }

        return new LocalModelEntry
        {
            Id = entry.Id,
            Engine = entry.Engine,
            Kind = entry.Kind,
            Role = entry.Role,
            Name = entry.Name,
            Description = entry.Description,
            FileName = entry.FileName,
            SizeBytes = entry.SizeBytes,
            Sha256 = entry.Sha256,
            Url = entry.Url,
            License = entry.License,
            Notes = entry.Notes,
            RunsOn = entry.RunsOn,
            MinVramBytes = entry.MinVramBytes,
            RecommendedFor = entry.RecommendedFor,
            AccuracyNote = entry.AccuracyNote,
            Llm = profile,
        };
    }
}
