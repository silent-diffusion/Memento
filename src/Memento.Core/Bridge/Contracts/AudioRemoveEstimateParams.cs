namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>audio.removeEstimate</c> (2.1).</summary>
public sealed record AudioRemoveEstimateParams
{
    /// <summary>The recordings to count; <c>null</c> for every recording with audio older than <see cref="OlderThanDays"/>.</summary>
    public IReadOnlyList<string>? RecordingIds { get; init; }

    /// <summary>With no ids: only recordings older than this many days; <c>null</c> for every one.</summary>
    public int? OlderThanDays { get; init; }
}
