namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>export.estimate</c>. Sizes of converted audio are estimates; <see cref="Files"/> counts <c>manifest.json</c>.</summary>
/// <param name="Unavailable">Ticked rows that cannot be written, worded for the dialog, e.g. "Transcript (not transcribed yet)".</param>
public sealed record ExportEstimate(int Files, long Bytes, IReadOnlyList<ExportEstimateItem> Items, IReadOnlyList<string> Unavailable);
