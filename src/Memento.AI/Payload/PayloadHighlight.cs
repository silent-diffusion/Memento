namespace Memento.AI.Payload;

/// <summary>A highlight: a moment (seconds), its text and the user's optional note.</summary>
public sealed record PayloadHighlight(double Start, string Text, double? End = null, string? Note = null);
