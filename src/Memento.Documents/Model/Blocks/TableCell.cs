using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Blocks;

public sealed record TableCell
{
    public IReadOnlyList<Run> Runs { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static TableCell Of(params Run[] runs) => new() { Runs = runs };

    public static TableCell Of(string text) => new() { Runs = [Run.Plain(text)] };
}
