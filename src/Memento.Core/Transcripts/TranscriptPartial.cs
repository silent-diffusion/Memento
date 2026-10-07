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
    string? Language,
    long ElapsedMs,
    Workers.WorkerDevice? Device = null)
{
    public const int CurrentSchemaVersion = 1;

    public static TranscriptPartial Empty(string signature) => new(CurrentSchemaVersion, signature, [], [], null, 0);

    /// <summary>The latest point (seconds on the timeline) up to which every transcribed track is final.</summary>
    public double LastSegmentEnd => Segments.Count == 0 ? 0 : Segments.Max(s => s.End);
}
