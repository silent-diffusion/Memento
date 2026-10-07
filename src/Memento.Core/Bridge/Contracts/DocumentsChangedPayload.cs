namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>documents.changed</c>.</summary>
/// <param name="Reason"><c>generated</c>, <c>edited</c> (also a rename), <c>created</c>, <c>deleted</c> or <c>restored</c>.</param>
public sealed record DocumentsChangedPayload(string RecordingId, string DocumentId, string Reason);
