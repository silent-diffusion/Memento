namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Payload of <c>recording.videoProblem</c>: the §18.6 toast, banner or card. Video fails first and alone; audio is never
/// affected by any of these.
/// </summary>
/// <param name="TrackId">The track concerned; for a source that could not start, the role (<c>screen</c>, <c>camera</c>).</param>
/// <param name="State">The track's state after it: <c>recording</c> (continues, e.g. in software), <c>waiting</c>, <c>ended</c> or <c>notStarted</c>.</param>
/// <param name="Code">One of the <c>video.*</c> error codes.</param>
/// <param name="Message">The sentence to show: the thing, the time, what is safe, the fix.</param>
/// <param name="AtMs">Recording position it happened at.</param>
public sealed record RecordingVideoProblemPayload(string SessionId, string TrackId, string Role, string State, string Code, string Message, long AtMs);
