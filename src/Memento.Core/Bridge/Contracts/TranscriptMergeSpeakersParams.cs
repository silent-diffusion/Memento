namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.mergeSpeakers</c>: every segment of <see cref="FromSpeakerId"/> moves to <see cref="IntoSpeakerId"/>.</summary>
public sealed record TranscriptMergeSpeakersParams
{
    public required string RecordingId { get; init; }

    public required string FromSpeakerId { get; init; }

    public required string IntoSpeakerId { get; init; }
}
