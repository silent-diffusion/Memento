namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>transcript.changed</c>.</summary>
/// <param name="Reason"><c>transcribed</c>, <c>edited</c>, <c>speakers</c>, <c>restored</c> or <c>topics</c>.</param>
public sealed record TranscriptChangedPayload(string RecordingId, int Version, string Reason);
