namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>processing.retry</c> and <c>processing.cancel</c>.</summary>
public sealed record ProcessingStageParams
{
    public required string RecordingId { get; init; }

    public required string Stage { get; init; }

    /// <summary>From <see cref="StageFailure.Remedies"/> (<c>cpu</c>, <c>model:small</c>, <c>retry</c>); retry only.</summary>
    public string? RemedyId { get; init; }
}
