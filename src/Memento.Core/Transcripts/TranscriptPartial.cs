using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// <c>transcript.partial.json</c>: what a pass has finished so far, written after every window. A pass that stops
/// (paused, cancelled, crashed, Memento closed) resumes from here when it runs again with the same model and language.
/// </summary>
/// <param name="Signature">Model, language and device kind; a pass with another signature starts over.</param>
/// <param name="Segments">Final segments, per track in time order.</param>
/// <param name="ElapsedMs">Processing time spent so far across attempts.</param>
/// <param name="Device">Where the finished windows were transcribed (kept when a resumed pass has nothing left to do).</param>
public sealed record TranscriptPartial(
    int SchemaVersion,
    string Signature,
    IReadOnlyList<TranscriptPartialTrack> Tracks,
    IReadOnlyList<TranscriptSegment> Segments,
    [property: JsonPropertyOrder(3)] string? Language,
    [property: JsonPropertyOrder(4)] long ElapsedMs,
    [property: JsonPropertyOrder(5)] Workers.WorkerDevice? Device = null)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<TranscriptPartialTrack> Tracks { get; set; } = Tracks ?? [];

    [JsonPropertyOrder(2)]
    public IReadOnlyList<TranscriptSegment> Segments { get; set; } = Segments ?? [];

    public const int CurrentSchemaVersion = 1;

    public static TranscriptPartial Empty(string signature) => new(CurrentSchemaVersion, signature, [], [], null, 0);

    /// <summary>The latest point (seconds on the timeline) up to which every transcribed track is final.</summary>
    [JsonPropertyOrder(6)]
    public double LastSegmentEnd => Segments.Count == 0 ? 0 : Segments.Max(s => s.End);
}
