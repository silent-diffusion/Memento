namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.list</c>: the recordings matching the filter, their summed duration and count.</summary>
public sealed record LibraryListResult(IReadOnlyList<RecordingSummary> Recordings, long TotalDurationMs, int TotalCount);
