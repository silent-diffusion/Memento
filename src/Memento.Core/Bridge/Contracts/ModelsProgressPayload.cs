namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>models.progress</c>.</summary>
/// <param name="State"><c>downloading</c>, <c>verifying</c>, <c>done</c> or <c>failed</c>.</param>
/// <param name="Message">Why it failed (or that it was cancelled), in words; otherwise <c>null</c>.</param>
public sealed record ModelsProgressPayload(string ModelId, int Percent, long BytesDone, long BytesTotal, string State, string? Message);
