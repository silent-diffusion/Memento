namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>documents.changed</c>.</summary>
/// <param name="Reason"><c>generated</c>, <c>edited</c>, <c>created</c>, <c>deleted</c>, <c>restored</c> or <c>renamed</c>.</param>
public sealed record DocumentsChangedPayload(string RecordingId, string DocumentId, string Reason);
