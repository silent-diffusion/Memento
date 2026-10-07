namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.restoreVersion</c>.</summary>
public sealed record DocumentRestoreParams(string RecordingId, string DocumentId, string VersionId);
