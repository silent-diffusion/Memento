namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>recording.setVideoSource</c>: start, switch or end the role's track.</summary>
public sealed record RecordingSetVideoSourceParams
{
    public required string SessionId { get; init; }

    /// <summary><c>screen</c> (a display or a window) or <c>camera</c>.</summary>
    public required string Role { get; init; }

    /// <summary>A <see cref="VideoSource.Id"/> of the role's kind, or <c>null</c> to end the role's track.</summary>
    public string? SourceId { get; init; }
}
