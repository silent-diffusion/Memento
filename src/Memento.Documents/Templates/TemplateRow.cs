using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Templates;

/// <summary>A row of one to three module cards laid out side by side.</summary>
public sealed record TemplateRow
{
    public IReadOnlyList<TemplateModule> Modules { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
