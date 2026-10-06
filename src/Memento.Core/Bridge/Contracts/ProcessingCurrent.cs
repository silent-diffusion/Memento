namespace Memento.Core.Bridge.Contracts;

/// <summary>The recording the processing card shows.</summary>
/// <param name="Stages">Every stage in pipeline order, including <c>stored</c>.</param>
public sealed record ProcessingCurrent(string RecordingId, string Title, RecordingSummary Meta, IReadOnlyList<StageStatus> Stages);
