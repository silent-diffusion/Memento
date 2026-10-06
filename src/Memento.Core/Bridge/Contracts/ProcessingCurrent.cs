namespace Memento.Core.Bridge.Contracts;

/// <summary>The recording the processing card shows.</summary>
/// <param name="Stages">Every stage in pipeline order, including <c>stored</c> and <c>optimize</c> (which <see cref="Meta"/> leaves out once done).</param>
public sealed record ProcessingCurrent(string RecordingId, string Title, RecordingSummary Meta, IReadOnlyList<StageStatus> Stages);
