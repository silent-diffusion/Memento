using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>2.0, <c>generation.copyPreview</c>: the preview of this template, or one of its passes, onto the clipboard.</summary>
/// <param name="PassId">One pass (<see cref="PreviewPass.Id"/>), or <c>null</c> for the whole preview.</param>
public sealed record GenerationCopyPreviewParams([property: JsonRequired] string RecordingId, [property: JsonRequired] Template Template, string? PassId = null);
