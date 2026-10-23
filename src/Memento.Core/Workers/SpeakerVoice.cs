using System.Text.Json.Serialization;

namespace Memento.Core.Workers;

/// <summary>
/// One voice found in a track, as the voice model hears it: the embedding of up to a minute of that speaker's turns.
/// Used to tell whether speakers on different tracks are the same person.
/// </summary>
/// <param name="Speaker">0-based speaker number within the track (as in <see cref="SpeakerTurn.Speaker"/>).</param>
/// <param name="Embedding">The voice model's embedding (192 values for TitaNet small).</param>
/// <param name="Seconds">How much speech the embedding was made from.</param>
public sealed record SpeakerVoice(int Speaker, IReadOnlyList<float> Embedding, [property: JsonPropertyOrder(2)] double Seconds)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<float> Embedding { get; set; } = Embedding ?? [];
}
