namespace Memento.AI.Payload;

/// <summary>A previously generated or written document of the recording, as plain text.</summary>
public sealed record PayloadDocument(string Title, string Text);
