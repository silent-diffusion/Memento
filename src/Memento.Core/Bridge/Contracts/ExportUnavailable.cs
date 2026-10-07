namespace Memento.Core.Bridge.Contracts;

/// <summary>A row of the Export dialog that cannot be written now (M3 clarification 1).</summary>
/// <param name="Component">A key of <see cref="ExportSelection"/>: <c>audioMixed</c>, <c>tracks</c>, <c>transcript</c>, <c>documents</c>, <c>details</c>, <c>attachments</c>.</param>
/// <param name="Reason">Why, for the dialog: "Not transcribed yet", "No attachments".</param>
public sealed record ExportUnavailable(string Component, string Reason);
