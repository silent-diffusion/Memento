namespace Memento.AI.Payload;

/// <summary>A participant from the recording details (not inferred).</summary>
public sealed record PayloadParticipant(string Name, string? Role = null);
