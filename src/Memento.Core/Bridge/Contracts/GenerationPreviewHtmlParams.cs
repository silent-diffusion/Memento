using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// <c>generation.previewHtml</c>: the Builder's preview paper with skeletons. <paramref name="RecordingId"/> is <c>null</c>
/// while a template is edited without a recording (Settings › Documents): the paper then shows a sample title and meta line.
/// </summary>
public sealed record GenerationPreviewHtmlParams(string? RecordingId, [property: JsonRequired] Template Template, [property: JsonRequired] string StyleId);
