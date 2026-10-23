using System.Text.Json.Serialization;

namespace Memento.Core.Workers;

/// <summary>The speaker turns found in one track, and each speaker's voice embedding.</summary>
/// <param name="Voices">One per speaker number that has enough speech; <c>null</c> from a worker that did not make them.</param>
/// <param name="AudioSeconds">Length of the track's audio.</param>
public sealed record DiarizedTrack(string TrackId, IReadOnlyList<SpeakerTurn> Turns, [property: JsonPropertyOrder(2)] IReadOnlyList<SpeakerVoice>? Voices = null, [property: JsonPropertyOrder(3)] double AudioSeconds = 0)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<SpeakerTurn> Turns { get; set; } = Turns ?? [];
}
