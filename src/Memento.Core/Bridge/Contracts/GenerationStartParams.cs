namespace Memento.Core.Bridge.Contracts;

/// <summary><c>generation.start</c>; <paramref name="DocumentId"/> regenerates into that document (its current content becomes a version).</summary>
public sealed record GenerationStartParams(string RecordingId, Template Template, string? DocumentId = null);
