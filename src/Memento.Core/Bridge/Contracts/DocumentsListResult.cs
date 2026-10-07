namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.list</c>: newest first.</summary>
public sealed record DocumentsListResult(IReadOnlyList<DocumentSummary> Documents);
