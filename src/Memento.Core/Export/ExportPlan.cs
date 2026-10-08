using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Export;

/// <summary>What one export of one recording would write, worked out from the selection and the project as it is now.</summary>
/// <param name="Unavailable">Ticked rows that cannot be written, and why.</param>
public sealed record ExportPlan(string RecordingId, string Title, string BaseName, DateTimeOffset ExportedAt, IReadOnlyList<ExportItem> Items, IReadOnlyList<ExportUnavailable> Unavailable)
{
    /// <summary>A generous estimate of <c>manifest.json</c>.</summary>
    public long ManifestEstimate => 400 + (Items.Count * 200L);

    /// <summary>The timestamps, speakers and layout of the Markdown and text transcript files, when the plan writes one.</summary>
    public TranscriptTextOptions? TranscriptOptions { get; init; }

    public long EstimatedBytes => Items.Sum(i => i.EstimatedBytes) + ManifestEstimate;
}
