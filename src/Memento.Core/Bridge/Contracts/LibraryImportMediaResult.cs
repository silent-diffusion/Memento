namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.importMedia</c>: the new recording (still being stored), or <c>null</c> when cancelled.</summary>
public sealed record LibraryImportMediaResult(string? RecordingId, bool Cancelled);
