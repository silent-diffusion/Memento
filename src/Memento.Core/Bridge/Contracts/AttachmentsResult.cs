namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>attachments.list</c>, oldest first.</summary>
public sealed record AttachmentsResult(IReadOnlyList<Attachment> Attachments);
