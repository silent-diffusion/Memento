using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › AI and privacy (M3, M4). External AI is off by default; keys live in DPAPI storage, never here.</summary>
public sealed record AiSettings
{
    /// <summary>
    /// "Allow external AI services": governs every provider the inputs would leave the PC for (Claude, ChatGPT, and
    /// "Your own server" at an address that is not this PC's). The local model and a server on this PC send nothing off
    /// the PC and are available whatever this says (M4, 2.5.0).
    /// </summary>
    public bool Enabled { get; init; }

    public bool AskBeforeSend { get; set; } = true;

    public bool KeepRecord { get; set; } = true;

    public AiShareSettings Share { get; set; } = new();

    /// <summary>The provider a template without its own uses (<c>anthropic</c>, <c>openai</c>, <c>server</c>, <c>local</c>), or <c>null</c> (M4).</summary>
    public string? DefaultProviderId { get; set; }

    /// <summary>The local model's catalog id, or <c>null</c> for the one this PC's hardware suits (M4).</summary>
    public string? LocalModelId { get; set; }

    /// <summary>Per-provider settings that are not keys: the model to use (M4).</summary>
    public AiProviderModels Providers { get; set; } = new();

    /// <summary>"Your own server" (2.5.0): its address, model and context. Its key is in the DPAPI store, never here.</summary>
    public AiServerSettings Server { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
