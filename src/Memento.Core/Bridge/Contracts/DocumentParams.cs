namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.get</c>, <c>documents.delete</c>, <c>documents.versions</c>.</summary>
public sealed record DocumentParams(string RecordingId, string DocumentId);
