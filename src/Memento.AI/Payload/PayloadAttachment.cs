namespace Memento.AI.Payload;

/// <summary>An attachment or imported document with its extracted text; <see cref="Text"/> is <c>null</c> when it has none.</summary>
public sealed record PayloadAttachment(string Name, string? MediaType, string? Text);
