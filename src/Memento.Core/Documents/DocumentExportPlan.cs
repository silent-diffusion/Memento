namespace Memento.Core.Documents;

/// <summary>What the Documents row of an export writes, and why anything ticked cannot be written.</summary>
/// <param name="Unavailable">A reason for the row when nothing can be written ("No documents"), otherwise <c>null</c>.</param>
public sealed record DocumentExportPlan(IReadOnlyList<DocumentExportFile> Files, string? Unavailable);
