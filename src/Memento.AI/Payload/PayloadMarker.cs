namespace Memento.AI.Payload;

/// <summary>A chapter or topic: where it starts (and ends, when known), in seconds, and its title.</summary>
public sealed record PayloadMarker(double Start, string Title, double? End = null);
