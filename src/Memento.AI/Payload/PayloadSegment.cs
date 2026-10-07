namespace Memento.AI.Payload;

/// <summary>One transcript segment (transcript.json <c>segments[]</c>): id, times in seconds, speaker id and text.</summary>
public sealed record PayloadSegment(string Id, double Start, double End, string? SpeakerId, string Text);
