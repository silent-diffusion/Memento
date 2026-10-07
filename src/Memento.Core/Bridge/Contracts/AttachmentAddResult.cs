namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>attachments.add</c>; <see cref="Attachment"/> is <c>null</c> when the picker was cancelled.</summary>
public sealed record AttachmentAddResult(Attachment? Attachment, bool Cancelled);
