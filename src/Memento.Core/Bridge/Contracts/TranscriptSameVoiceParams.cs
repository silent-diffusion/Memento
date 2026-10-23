namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>transcript.sameVoice</c> (2.6.0, reads only): which other lines were heard as the same voice as
/// <see cref="SegmentIds"/> by the last speaker pass (<c>voices.json</c>).
/// </summary>
public sealed record TranscriptSameVoiceParams
{
    public required string RecordingId { get; init; }

    public required IReadOnlyList<string> SegmentIds { get; init; }
}
