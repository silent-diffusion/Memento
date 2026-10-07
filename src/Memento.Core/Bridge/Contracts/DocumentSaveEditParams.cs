namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.saveEdit</c>: the viewer's paper markup after light edits.</summary>
public sealed record DocumentSaveEditParams(string RecordingId, string DocumentId, string Html);
