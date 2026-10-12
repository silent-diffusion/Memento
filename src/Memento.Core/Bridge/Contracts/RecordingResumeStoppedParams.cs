namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>recording.resumeStopped</c> (2.1). Without <see cref="SourceIds"/> the last segment's audio and video
/// sources that are still connected are recorded; with them, exactly those (at least one audio source).
/// </summary>
public sealed record RecordingResumeStoppedParams
{
    public required string RecordingId { get; init; }

    public IReadOnlyList<string>? SourceIds { get; init; }
}
