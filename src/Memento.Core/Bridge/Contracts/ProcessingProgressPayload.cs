namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>processing.progress</c>. Only the <c>stored</c> stage exists in M1.</summary>
public sealed record ProcessingProgressPayload(string RecordingId, IReadOnlyList<StageStatus> Stages);
