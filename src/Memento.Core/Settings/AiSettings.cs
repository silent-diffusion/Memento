using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › AI and privacy (M3). Off by default; keys live in DPAPI storage, never here.</summary>
public sealed record AiSettings
{
    public bool Enabled { get; init; }

    public bool AskBeforeSend { get; init; } = true;

    public bool KeepRecord { get; init; } = true;

    public AiShareSettings Share { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
