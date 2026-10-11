namespace Memento.Core.Video;

/// <summary>Something about one video track the user must be told (DESIGN.md §18.6).</summary>
/// <param name="Role"><c>screen</c> or <c>camera</c>: the track as the page knows it.</param>
/// <param name="State">The track's state after it (<see cref="VideoProblemCodes.StateOf"/>).</param>
/// <param name="Code">One of <see cref="VideoProblemCodes"/>.</param>
/// <param name="Detail">The other app (camera in use), the switch that is off (camera privacy), the software encoder (fallback); otherwise null.</param>
/// <param name="Message">The sentence: the thing, the time, what is safe, the fix.</param>
/// <param name="AtMs">Recording position it happened at.</param>
public sealed class VideoProblemEventArgs(string trackId, string role, string state, string code, string sourceId, string sourceName, string? detail, string message, long atMs) : EventArgs
{
    public string TrackId { get; } = trackId;

    public string Role { get; } = role;

    public string State { get; } = state;

    public string Code { get; } = code;

    public string SourceId { get; } = sourceId;

    public string SourceName { get; } = sourceName;

    public string? Detail { get; } = detail;

    public string Message { get; } = message;

    public long AtMs { get; } = atMs;
}
