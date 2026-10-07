namespace Memento.Core.Bridge.Contracts;

/// <summary><c>generation.previewHtml</c>: the Builder's preview paper with skeletons.</summary>
public sealed record GenerationPreviewHtmlParams(string RecordingId, Template Template, string StyleId);
