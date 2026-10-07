namespace Memento.AI.Payload;

/// <summary>An identified speaker: the transcript id and the name exactly as the transcript shows it.</summary>
public sealed record PayloadSpeaker(string Id, string Name);
