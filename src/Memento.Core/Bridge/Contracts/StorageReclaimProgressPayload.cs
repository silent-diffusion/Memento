namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>storage.reclaimProgress</c>.</summary>
/// <param name="State"><c>running</c>, <c>done</c> or <c>failed</c>.</param>
public sealed record StorageReclaimProgressPayload(string JobId, int Percent, string State, string? Message, int RecordingsDone, long BytesFreed);
