namespace Memento.Core.Bridge.Contracts;

/// <summary>A file kept with a recording in its <c>attachments/</c> folder, original bytes (BRIDGE.md M3).</summary>
/// <param name="Kind"><c>agenda</c> for an imported agenda's original file, <c>file</c> for anything else.</param>
/// <param name="ContentType">The MIME type from the file name, or <c>null</c> when unknown.</param>
public sealed record Attachment(string Id, string Name, long SizeBytes, DateTimeOffset AddedAt, string Kind, string? ContentType);
