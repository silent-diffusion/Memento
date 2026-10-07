using Memento.Documents.Model;
using Memento.Documents.Model.Records;

namespace Memento.Generation.Generation;

/// <summary>The rows of the new document and the record of how they were made.</summary>
public sealed record PipelineOutcome(
    IReadOnlyList<DocumentRow> Rows,
    IReadOnlyList<RecordModule> Modules,
    IReadOnlyList<RecordClaim> Claims,
    IReadOnlyList<RecordRequest> Requests,
    RecordTimings Timings,
    int Chunks,
    IReadOnlyList<string> Warnings);
