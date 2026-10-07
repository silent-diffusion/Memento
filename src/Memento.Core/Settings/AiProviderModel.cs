using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>One cloud provider's model override; <c>null</c> uses <see cref="Ai.AiModelDefaults"/>.</summary>
public sealed record AiProviderModel
{
    public string? Model { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
