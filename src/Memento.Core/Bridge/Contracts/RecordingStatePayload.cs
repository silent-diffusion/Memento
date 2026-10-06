namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>recording.state</c>, sent on every change and at least every second while recording.</summary>
/// <param name="State"><c>recording</c>, <c>paused</c>, <c>finalizing</c>, <c>ready</c> or <c>stopped</c>.</param>
/// <param name="ElapsedMs">Recorded time, excluding pauses.</param>
public sealed record RecordingStatePayload(
    string SessionId,
    string RecordingId,
    string State,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    IReadOnlyList<Track> Tracks,
    DateTimeOffset? LastCheckpointAt,
    int HighlightsCount);
