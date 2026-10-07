using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>generation.previewHtml</c>: the Builder's preview paper with skeletons.</summary>
public sealed record GenerationPreviewHtmlParams([property: JsonRequired] string RecordingId, [property: JsonRequired] Template Template, [property: JsonRequired] string StyleId);
