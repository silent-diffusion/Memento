namespace Memento.Core.Bridge.Contracts;

/// <summary>A speaker as it was and the lines to give back to it (<c>transcript.restoreSpeakers</c>).</summary>
public sealed record SpeakerLines
{
    public required SpeakerRestore Speaker { get; init; }

    public required IReadOnlyList<string> SegmentIds { get; init; }
}
