namespace Memento.Core.Documents;

/// <summary>
/// The Export dialog's Documents row (<c>export.run</c> with <c>documents</c> ticked): the files the recording's documents
/// become in the chosen format. Implemented with the M4 document store and exporters (Memento.Generation); Core's export
/// planner only places the files.
/// </summary>
public interface IDocumentExportSource
{
    /// <param name="documentIds">The documents to export; empty means every document of the recording.</param>
    /// <param name="format"><c>docx</c>, <c>pdf</c> or <c>markdown</c>.</param>
    Task<DocumentExportPlan> PlanAsync(string recordingId, IReadOnlyList<string> documentIds, string format, CancellationToken cancellationToken);
}
