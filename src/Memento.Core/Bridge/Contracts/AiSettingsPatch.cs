using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › AI and privacy for <c>settings.set</c>. Omitted fields keep their value.</summary>
public sealed record AiSettingsPatch
{
    public bool? Enabled { get; init; }

    public bool? AskBeforeSend { get; init; }

    public bool? KeepRecord { get; init; }

    public AiSharePatch? Share { get; init; }

    /// <summary>M4: <c>"anthropic"</c>, <c>"openai"</c>, <c>"local"</c>, or <c>null</c> to clear; omitted keeps it.</summary>
    public JsonElement DefaultProviderId { get; init; }

    /// <summary>M4: an installed local model's catalog id, or <c>null</c> to go back to the one the hardware suits.</summary>
    public JsonElement LocalModelId { get; init; }

    /// <summary>M4: the model per cloud provider. Keys still change only through <c>ai.setKey</c> and <c>ai.clearKey</c>.</summary>
    public AiProvidersPatch? Providers { get; init; }
}
