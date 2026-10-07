using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>generation.start</c>; <paramref name="DocumentId"/> regenerates into that document (its current content becomes a version).</summary>
public sealed record GenerationStartParams([property: JsonRequired] string RecordingId, [property: JsonRequired] Template Template, string? DocumentId = null);
