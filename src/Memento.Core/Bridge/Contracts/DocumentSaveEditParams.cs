using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.saveEdit</c>: the viewer's paper markup after light edits.</summary>
public sealed record DocumentSaveEditParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string DocumentId, [property: JsonRequired] string Html);
