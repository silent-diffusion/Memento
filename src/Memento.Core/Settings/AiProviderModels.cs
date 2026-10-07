using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>The model chosen for each cloud provider (Settings › AI and privacy › Providers, M4). Keys are never here.</summary>
public sealed record AiProviderModels
{
    public AiProviderModel Anthropic { get; set; } = new();

    public AiProviderModel Openai { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
