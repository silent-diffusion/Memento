namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// What the Library and Review say about a recording's video (DESIGN.md §18.3, §18.7): its tracks, size and shape, or
/// that it was removed. <c>null</c> on <see cref="RecordingSummary"/> and <see cref="Project"/> for an audio recording.
/// </summary>
/// <param name="State"><c>present</c> or <c>removed</c>.</param>
/// <param name="Layout"><c>screen</c>, <c>camera</c> or <c>screenAndCamera</c>: which roles were recorded.</param>
/// <param name="Width">The screen track's size (the camera's when there is no screen track).</param>
/// <param name="DurationMs">The longest video track.</param>
/// <param name="SizeBytes">The video files on disk; 0 once removed.</param>
/// <param name="RemovedAt">When "Remove video, keep audio" deleted the files; <c>null</c> while present.</param>
/// <param name="BytesFreed">What the removal freed; <c>null</c> while present.</param>
public sealed record VideoSummary(
    string State,
    string Layout,
    int Width,
    int Height,
    int FrameRate,
    long DurationMs,
    long SizeBytes,
    DateTimeOffset? RemovedAt,
    long? BytesFreed);
