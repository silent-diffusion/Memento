namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.reduceSpeakers</c>: merge the most alike speakers until <see cref="Count"/> are left.</summary>
public sealed record TranscriptReduceSpeakersParams
{
    public required string RecordingId { get; init; }

    /// <summary>1–20.</summary>
    public required int Count { get; init; }
}
