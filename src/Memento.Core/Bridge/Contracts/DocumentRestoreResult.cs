namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.restoreVersion</c>: the document as it is now (the content it replaced is kept as a version).</summary>
public sealed record DocumentRestoreResult(DocumentContent Document)
{
    public DocumentSummary? Summary { get; init; }
}
