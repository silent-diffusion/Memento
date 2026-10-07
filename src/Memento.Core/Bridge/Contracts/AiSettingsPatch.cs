using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › AI and privacy for <c>settings.set</c>. Omitted fields keep their value.</summary>
public sealed record AiSettingsPatch
{
    public bool? Enabled { get; init; }

    public bool? AskBeforeSend { get; init; }

    public bool? KeepRecord { get; init; }

    public AiSharePatch? Share { get; init; }

    /// <summary>Accepted and ignored, so the UI can send the block it read back; keys change only through <c>ai.setKey</c>.</summary>
    public JsonElement? Providers { get; init; }
}
