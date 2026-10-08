namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.getVersion</c>: a kept version's content and the viewer's paper for it (read-only).</summary>
/// <param name="Html">The paper as <c>documents.renderHtml</c> with <c>mode: view</c> draws it, for this version.</param>
public sealed record DocumentVersionResult(DocumentContent Document, string Html);
