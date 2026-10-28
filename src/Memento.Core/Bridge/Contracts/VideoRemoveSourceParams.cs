namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>video.removeSource</c> (2.8.0): stop one video track from now.</summary>
public sealed record VideoRemoveSourceParams
{
    public required string SessionId { get; init; }

    /// <summary>A <see cref="VideoTrackStatus.TrackId"/>: <c>screen</c>, <c>screen-2</c>, <c>camera</c>.</summary>
    public required string TrackId { get; init; }
}
