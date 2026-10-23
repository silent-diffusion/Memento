using System.Text.Json.Serialization;

namespace Memento.Core.Transcripts;

/// <summary>
/// One voice the speakers stage found on one track (a sherpa-onnx cluster), kept in <c>voices.json</c> with the lines it
/// was given, so Review can tell which speakers sound most alike later (<c>transcript.reduceSpeakers</c>).
/// </summary>
/// <param name="Track">The track it was heard on.</param>
/// <param name="Speaker">The cluster number within that track.</param>
/// <param name="Embedding">The voice model's embedding of up to a minute of its turns; empty when there was too little speech.</param>
/// <param name="Seconds">How much speech the embedding was made from.</param>
/// <param name="SegmentIds">The transcript lines this voice won when speakers were identified.</param>
public sealed record VoiceCluster(string Track, int Speaker, IReadOnlyList<float> Embedding, [property: JsonPropertyOrder(2)] double Seconds, IReadOnlyList<string> SegmentIds)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<float> Embedding { get; set; } = Embedding ?? [];

    /// <summary>
    /// How well the voice fits the person it was grouped into (2.6.0): the cosine of its embedding to the speech-weighted mix
    /// of that person's other voices, kept so the uncertain marks it gives can be tuned from the file. <c>null</c> (and left
    /// out of the JSON) for a voice alone in its person, a voice without an embedding, and every file from before.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Fit { get; init; }

    [JsonPropertyOrder(4)]
    public IReadOnlyList<string> SegmentIds { get; set; } = SegmentIds ?? [];
}
