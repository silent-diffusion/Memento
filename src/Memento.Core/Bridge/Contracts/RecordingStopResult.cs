namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>recording.stop</c>; finalize has started when this returns.</summary>
public sealed record RecordingStopResult(string RecordingId);
