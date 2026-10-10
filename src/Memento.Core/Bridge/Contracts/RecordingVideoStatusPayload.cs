namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>recording.videoStatus</c>: once a second while a session with video records, beside <c>recording.state</c>.</summary>
public sealed record RecordingVideoStatusPayload(string SessionId, IReadOnlyList<VideoTrackStatus> VideoTracks);
