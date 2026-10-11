namespace Memento.Core.Bridge.Contracts;

/// <summary>The recording the processing card shows.</summary>
/// <param name="Stages">Every stage in pipeline order, including <c>stored</c> and <c>optimize</c> (which <see cref="Meta"/> leaves out once done).</param>
/// <param name="Failure">2.0: the video stage failed (the §18.6 "Video encoding failed" card, with its remedies); <c>null</c> otherwise.</param>
public sealed record ProcessingCurrent(string RecordingId, string Title, RecordingSummary Meta, IReadOnlyList<StageStatus> Stages, StageFailure? Failure = null);
