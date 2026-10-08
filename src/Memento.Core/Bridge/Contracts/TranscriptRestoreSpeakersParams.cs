namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>transcript.restoreSpeakers</c>: several <c>transcript.restoreSpeaker</c> steps in the order given, written
/// once (the Undo of <c>transcript.reduceSpeakers</c>, its merges last first).
/// </summary>
public sealed record TranscriptRestoreSpeakersParams
{
    public required string RecordingId { get; init; }

    public required IReadOnlyList<SpeakerLines> Speakers { get; init; }
}
