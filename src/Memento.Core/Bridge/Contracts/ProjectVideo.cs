namespace Memento.Core.Bridge.Contracts;

/// <summary><c>Project.video</c>: the video tracks Review plays; <c>null</c> when the recording never had video.</summary>
/// <param name="Tracks">Screen first; <c>[]</c> once removed.</param>
/// <param name="Pip">Where the inset was when recording ended; Review starts there.</param>
/// <param name="SizeBytes">Every video file of the recording.</param>
public sealed record ProjectVideo(IReadOnlyList<ProjectVideoTrack> Tracks, PipState Pip, long SizeBytes, VideoRemovedInfo? Removed);
