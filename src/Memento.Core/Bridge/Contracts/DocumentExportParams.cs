namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.export</c>; without <paramref name="Path"/> the file goes to the Settings export folder (or Documents).</summary>
/// <param name="Format"><c>docx</c>, <c>pdf</c> or <c>markdown</c>.</param>
public sealed record DocumentExportParams(string RecordingId, string DocumentId, string Format, string? Path = null);
