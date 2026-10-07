using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Speakers for <c>settings.set</c>. Each omitted or <c>null</c> field keeps its value.</summary>
public sealed record SpeakersSettingsPatch
{
    public bool? Identify { get; init; }

    /// <summary>The string <c>auto</c> or a number 1–20.</summary>
    public JsonElement? ExpectedSpeakers { get; init; }

    public bool? RememberRenamed { get; init; }

    public string? EmbeddingModelId { get; init; }
}
