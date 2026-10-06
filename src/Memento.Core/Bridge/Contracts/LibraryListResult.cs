namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.list</c>: every recording, newest first, and their summed duration.</summary>
public sealed record LibraryListResult(IReadOnlyList<RecordingSummary> Recordings, long TotalDurationMs);
