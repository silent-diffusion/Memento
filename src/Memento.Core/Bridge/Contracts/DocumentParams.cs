using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.get</c>, <c>documents.delete</c>, <c>documents.versions</c>.</summary>
public sealed record DocumentParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string DocumentId);
