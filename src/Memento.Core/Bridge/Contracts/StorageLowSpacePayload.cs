namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>storage.lowSpace</c> (the low disk space banner).</summary>
public sealed record StorageLowSpacePayload(long FreeBytes, long ThresholdBytes, bool RecordingContinues, bool TranscriptionPaused);
