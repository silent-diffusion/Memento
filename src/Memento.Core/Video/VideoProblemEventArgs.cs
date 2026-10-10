namespace Memento.Core.Video;

/// <summary>Something about one video track the user must be told (DESIGN.md §18.6).</summary>
/// <param name="State">The track's state after it: <c>recording</c>, <c>waiting</c> or <c>ended</c>.</param>
/// <param name="Code">A <c>video.*</c> bridge error code.</param>
/// <param name="Message">The sentence: the thing, the time, what is safe, the fix.</param>
/// <param name="AtMs">Recording position it happened at.</param>
public sealed class VideoProblemEventArgs(string trackId, string role, string state, string code, string message, long atMs) : EventArgs
{
    public string TrackId { get; } = trackId;

    public string Role { get; } = role;

    public string State { get; } = state;

    public string Code { get; } = code;

    public string Message { get; } = message;

    public long AtMs { get; } = atMs;
}
