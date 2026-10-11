namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Payload of <c>recording.videoStatus</c>: once a second while a session with video records, beside
/// <c>recording.state</c>. A track that is off is left out; an ended one stays until the session ends. <see cref="Pip"/>
/// is the inset as the session has it, so a page that rejoins draws it where it is.
/// </summary>
public sealed record RecordingVideoStatusPayload(string SessionId, IReadOnlyList<VideoTrackStatus> VideoTracks, PipState Pip);
