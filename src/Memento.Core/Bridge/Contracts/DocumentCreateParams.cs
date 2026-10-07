using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.create</c>: a hand-written document with one empty text block.</summary>
public sealed record DocumentCreateParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string Name, string? StyleId);
