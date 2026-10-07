namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.create</c>: a hand-written document with one empty text block.</summary>
public sealed record DocumentCreateParams(string RecordingId, string Name, string? StyleId);
