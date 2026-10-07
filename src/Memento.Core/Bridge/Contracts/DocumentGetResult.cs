namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.get</c>.</summary>
public sealed record DocumentGetResult(DocumentContent Document, DocumentSummary Summary);
