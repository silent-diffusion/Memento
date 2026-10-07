namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.saveEdit</c>.</summary>
public sealed record DocumentSaveEditResult(DocumentContent Document, int Version);
