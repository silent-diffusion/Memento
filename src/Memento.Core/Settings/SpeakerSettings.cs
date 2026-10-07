using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Settings › Speakers (M2).</summary>
public sealed record SpeakerSettings
{
    public const string DefaultEmbeddingModelId = "nemo-titanet-small";
    public const int MaxExpectedSpeakers = 20;

    /// <summary>Identify who speaks after transcription.</summary>
    public bool Identify { get; init; } = true;

    /// <summary>How many speakers to look for; <c>null</c> finds out automatically.</summary>
    public int? ExpectedSpeakers { get; init; }

    /// <summary>Remember renamed speakers across recordings by voice. Stored; not applied in this version.</summary>
    public bool RememberRenamed { get; init; }

    public string EmbeddingModelId { get; init; } = DefaultEmbeddingModelId;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (ExpectedSpeakers is < 1 or > MaxExpectedSpeakers)
        {
            return $"Expected speakers {ExpectedSpeakers} is out of range. Choose auto or 1 to {MaxExpectedSpeakers}.";
        }

        if (EmbeddingModelId.Length is 0 or > 64)
        {
            return "The voice model id is not valid; choose a model again.";
        }

        return null;
    }
}
