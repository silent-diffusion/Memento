namespace Memento.Core.Bridge.Contracts;

/// <summary>The largest recording in the library.</summary>
public sealed record LibraryUsageLargest(string RecordingId, string Title, long SizeBytes);
