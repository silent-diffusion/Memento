using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Library;

/// <summary>The recordings matching a <see cref="LibraryQuery"/> and their totals.</summary>
public sealed record LibraryQueryResult(IReadOnlyList<RecordingSummary> Recordings, long TotalDurationMs, int TotalCount);
