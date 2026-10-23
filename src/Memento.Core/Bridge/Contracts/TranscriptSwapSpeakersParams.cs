namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>transcript.swapSpeakers</c> (2.6.0): every line of <see cref="SpeakerId"/> takes
/// <see cref="OtherSpeakerId"/> and the other way round, from <see cref="FromSegmentId"/> (inclusive, in transcript order)
/// to the end, or everywhere when it is <c>null</c>. Names, ids and colours stay.
/// </summary>
public sealed record TranscriptSwapSpeakersParams
{
    public required string RecordingId { get; init; }

    public required string SpeakerId { get; init; }

    public required string OtherSpeakerId { get; init; }

    public string? FromSegmentId { get; init; }
}
