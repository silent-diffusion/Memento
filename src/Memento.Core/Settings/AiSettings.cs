using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › AI and privacy (M3, M4). External AI is off by default; keys live in DPAPI storage, never here.</summary>
public sealed record AiSettings
{
    /// <summary>
    /// "Allow external AI services": governs the cloud providers (Claude, ChatGPT) only. The local model sends nothing
    /// and is available whenever its model is installed, whatever this says (M4).
    /// </summary>
    public bool Enabled { get; init; }

    public bool AskBeforeSend { get; set; } = true;

    public bool KeepRecord { get; set; } = true;

    public AiShareSettings Share { get; set; } = new();

    /// <summary>The provider a template without its own uses (<c>anthropic</c>, <c>openai</c>, <c>local</c>), or <c>null</c> (M4).</summary>
    public string? DefaultProviderId { get; set; }

    /// <summary>The local model's catalog id, or <c>null</c> for the one this PC's hardware suits (M4).</summary>
    public string? LocalModelId { get; set; }

    /// <summary>Per-provider settings that are not keys: the model to use (M4).</summary>
    public AiProviderModels Providers { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
