namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>library.changed</c>, after any project write; the UI refetches.</summary>
public sealed record LibraryChangedPayload(IReadOnlyList<string> RecordingIds);
