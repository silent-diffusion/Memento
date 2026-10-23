using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

public sealed record LabelValuePair
{
    public string Label { get; set; } = string.Empty;

    public IReadOnlyList<Run> Runs { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
