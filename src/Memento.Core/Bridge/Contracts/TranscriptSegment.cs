using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>One transcript segment (BRIDGE.md M2). Times are seconds on the recording timeline.</summary>
/// <param name="Track">Id of the track the words came from (<c>mic</c>, <c>system</c>, …); <c>null</c> for a mix.</param>
/// <param name="Speaker">Speaker id, or <c>null</c> when speakers were not identified.</param>
/// <param name="SpeakerConfidence">How sure the speaker assignment is, 0..1; below 0.7 the UI marks it uncertain.</param>
/// <param name="Confidence">The lowest word confidence.</param>
/// <param name="Words">Empty when word timestamps are off.</param>
/// <remarks>
/// <see cref="SpeakerByUser"/> (2.6.0) is true once the user placed the line on its speaker (a line's menu, selection mode,
/// a swap): the next speakers pass keeps that speaker and pins the line's raw voice to it (BRIDGE.md › Corrections guide
/// the next pass). The speakers stage's own assignments leave it false, and it is left out of the JSON while false, so
/// older files read as before.
/// </remarks>
public sealed record TranscriptSegment(
    string Id,
    double Start,
    double End,
    string? Track,
    string? Speaker,
    double? SpeakerConfidence,
    string Text,
    double Confidence,
    IReadOnlyList<TranscriptWord> Words,
    [property: JsonPropertyOrder(2)] TranscriptEdit? Edited)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<TranscriptWord> Words { get; set; } = Words ?? [];

    /// <summary>The user placed this line on its speaker; the next pass keeps it and pins its voice (2.6.0).</summary>
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SpeakerByUser { get; init; }
}
