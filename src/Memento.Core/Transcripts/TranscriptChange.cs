namespace Memento.Core.Transcripts;

/// <summary>The last write that defined a transcript's content (a speakers pass keeps the one before it).</summary>
public sealed record TranscriptChange(string Reason, DateTimeOffset At);
