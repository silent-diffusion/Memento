namespace Memento.Core.Workers;

/// <summary>The end of a transcription job. Segments arrived earlier in <c>progress</c> lines.</summary>
public sealed record TranscribeResult(string Language, bool LanguageDetected, WorkerDevice Device, double AudioSeconds, long ElapsedMs);
