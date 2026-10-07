namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>recording.liveTranscript</c>. Optional, and this build never sends it.</summary>
public sealed record LiveTranscriptPayload(string SessionId, IReadOnlyList<LiveTranscriptSegment> Segments);
