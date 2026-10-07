namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.versions</c>: newest first; empty when history is off.</summary>
public sealed record DocumentVersionsResult(IReadOnlyList<DocumentVersionInfo> Versions);
