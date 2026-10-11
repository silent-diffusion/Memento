namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Payload of <c>recording.videoProblem</c>: the §18.6 toast, banner or card. Video fails first and alone; audio is never
/// affected. The page writes the sentence from <see cref="Code"/>; <see cref="Message"/> is the host's own, the fallback.
/// </summary>
/// <param name="TrackId"><c>screen</c> or <c>camera</c>.</param>
/// <param name="State">The track's state after it (BRIDGE.md's table of codes).</param>
/// <param name="Code">One of the closed list of problem codes (<c>VideoProblemCodes</c>).</param>
/// <param name="Detail">The other app (camera in use), the switch that is off (camera privacy), the software encoder (fallback); otherwise <c>null</c>.</param>
/// <param name="AtMs">Recording position it happened at.</param>
public sealed record RecordingVideoProblemPayload(string SessionId, string TrackId, string State, string Code, string SourceId, string SourceName, string? Detail, string Message, long AtMs);
