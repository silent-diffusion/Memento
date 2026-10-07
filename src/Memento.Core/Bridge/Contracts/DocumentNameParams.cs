using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.rename</c> (name required), <c>documents.duplicate</c> (name optional), <c>documents.makeTemplate</c>.</summary>
public sealed record DocumentNameParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string DocumentId, string? Name = null);
