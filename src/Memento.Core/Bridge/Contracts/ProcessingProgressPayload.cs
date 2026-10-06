namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>processing.progress</c>: every stage in pipeline order. M1 runs <c>stored</c> and, for a smaller storage format, <c>optimize</c>.</summary>
public sealed record ProcessingProgressPayload(string RecordingId, IReadOnlyList<StageStatus> Stages);
