namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>transcript.restoreSpeaker</c>: puts <see cref="Speaker"/> back (added when the transcript does not have
/// its id, otherwise its name, colour and renamed flag are set) and assigns <see cref="SegmentIds"/> to it. The inverse of a
/// merge, a rename or a removal, for Undo.
/// </summary>
public sealed record TranscriptRestoreSpeakerParams
{
    public required string RecordingId { get; init; }

    public required SpeakerRestore Speaker { get; init; }

    public required IReadOnlyList<string> SegmentIds { get; init; }
}
