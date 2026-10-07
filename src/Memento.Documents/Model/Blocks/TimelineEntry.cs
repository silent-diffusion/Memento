using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

public sealed record TimelineEntry
{
    /// <summary>The moment in seconds.</summary>
    public double T { get; init; }

    public IReadOnlyList<Run> Runs { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
