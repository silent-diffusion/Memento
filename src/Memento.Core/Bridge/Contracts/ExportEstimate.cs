namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Result of <c>export.estimate</c>. <see cref="Items"/> lists every file of every ticked row (the UI asks once with
/// everything ticked and sums the rows it shows); <see cref="Files"/> and <see cref="Bytes"/> also count
/// <c>manifest.json</c>. Sizes of converted audio are estimates.
/// </summary>
public sealed record ExportEstimate(int Files, long Bytes, IReadOnlyList<ExportEstimateItem> Items, IReadOnlyList<ExportUnavailable> Unavailable);
