namespace Memento.AI.Payload;

/// <summary>A user annotation or note; <see cref="At"/> (seconds) ties it to a moment.</summary>
public sealed record PayloadNote(string Text, double? At = null);
