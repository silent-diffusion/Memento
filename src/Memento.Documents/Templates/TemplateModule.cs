using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;

namespace Memento.Documents.Templates;

/// <summary>One module card in a template (ARCHITECTURE.md §8): type, instructions, length, text size and transcript linking.</summary>
public sealed record TemplateModule
{
    /// <summary>Unique within the template; becomes the generated <see cref="ModuleBlock.Id"/>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The module type (<see cref="ModuleIds"/>).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The heading; <c>null</c> uses the catalog's display name.</summary>
    public string? Title { get; init; }

    public string Instructions { get; init; } = string.Empty;

    public ModuleLength Length { get; init; } = ModuleLength.Medium;

    public TextSize TextSize { get; init; } = TextSize.Normal;

    public bool LinkToTranscript { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The heading the document will show.</summary>
    public string ResolveTitle(ModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return !string.IsNullOrWhiteSpace(Title) ? Title! : catalog.Find(Type)?.DisplayName ?? Type;
    }

    /// <summary>A new card for a catalog module with its defaults.</summary>
    public static TemplateModule FromCatalog(ModuleDefinition definition, string id)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new()
        {
            Id = id,
            Type = definition.Id,
            Title = definition.DisplayName,
            Instructions = definition.DefaultInstructions,
            Length = definition.DefaultLength,
            LinkToTranscript = definition.DefaultLinkToTranscript,
        };
    }
}
