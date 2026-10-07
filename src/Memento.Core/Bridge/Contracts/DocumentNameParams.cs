namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.rename</c> (name required), <c>documents.duplicate</c> (name optional), <c>documents.makeTemplate</c>.</summary>
public sealed record DocumentNameParams(string RecordingId, string DocumentId, string? Name = null);
