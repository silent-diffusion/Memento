using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>A row of one to three modules side by side (two-column grid in Word and PDF; stacked in Markdown).</summary>
public sealed record DocumentRow
{
    public const int MaxModules = 3;

    public IReadOnlyList<ModuleBlock> Modules { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static DocumentRow Of(params ModuleBlock[] modules) => new() { Modules = modules };
}
