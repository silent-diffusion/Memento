namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>recording.setVideoSource</c>: every video track of the session, ended ones included.</summary>
public sealed record VideoTracksResult(IReadOnlyList<VideoTrackStatus> VideoTracks);
