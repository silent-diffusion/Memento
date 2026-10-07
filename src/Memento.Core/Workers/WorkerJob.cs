namespace Memento.Core.Workers;

/// <summary>The job in a <c>start</c> command; exactly one of the bodies is set, as <see cref="Kind"/> says.</summary>
public sealed record WorkerJob(string Kind, TranscribeJob? Transcribe = null, DiarizeJob? Diarize = null);
