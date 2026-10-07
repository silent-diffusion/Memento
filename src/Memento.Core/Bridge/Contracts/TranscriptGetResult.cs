namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.get</c>.</summary>
/// <param name="Transcript"><c>null</c> until the first pass completes; a failed pass may leave a partial one.</param>
/// <param name="Status"><c>none</c>, <c>queued</c>, <c>running</c>, <c>done</c>, <c>failed</c> or <c>paused</c>.</param>
public sealed record TranscriptGetResult(Transcript? Transcript, string Status, StageFailure? Failure);
