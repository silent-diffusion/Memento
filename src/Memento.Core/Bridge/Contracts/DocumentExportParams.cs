using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.export</c>; without <paramref name="Path"/> the file goes to the Settings export folder (or Documents).</summary>
/// <param name="Format"><c>docx</c>, <c>pdf</c> or <c>markdown</c>.</param>
public sealed record DocumentExportParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string DocumentId, [property: JsonRequired] string Format, string? Path = null);
