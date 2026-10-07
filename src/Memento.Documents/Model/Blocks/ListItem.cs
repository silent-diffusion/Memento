using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

/// <summary>One list item and the items nested under it.</summary>
public sealed record ListItem
{
    public IReadOnlyList<Run> Runs { get; init; } = [];

    public IReadOnlyList<ListItem> Items { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
