namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>export.estimate</c>.</summary>
public sealed record ExportEstimateParams
{
    public required string RecordingId { get; init; }

    public required ExportSelection Selection { get; init; }
}
