namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>recording.stoppedByHost</c>.</summary>
/// <param name="Reason"><c>diskFull</c>, <c>deviceLost</c> or <c>error</c>.</param>
/// <param name="AtMs">Recorded time at which the recording stopped.</param>
/// <param name="Message">Ready-to-show copy (DESIGN.md §17).</param>
public sealed record StoppedByHostPayload(string SessionId, string RecordingId, string Reason, long AtMs, string Message);
