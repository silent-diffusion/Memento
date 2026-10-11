namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>video.removeEstimate</c>.</summary>
public sealed record VideoRemoveEstimateParams
{
    /// <summary>The recordings to count; <c>null</c> for every recording with video older than <see cref="OlderThanDays"/>.</summary>
    public IReadOnlyList<string>? RecordingIds { get; init; }

    /// <summary>With no ids: only recordings older than this many days; <c>null</c> for every one.</summary>
    public int? OlderThanDays { get; init; }
}
