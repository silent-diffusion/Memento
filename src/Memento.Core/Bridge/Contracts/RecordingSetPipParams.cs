namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>recording.setPip</c>: the camera inset on or off, and its corner.</summary>
public sealed record RecordingSetPipParams
{
    public required string SessionId { get; init; }

    public bool Enabled { get; init; }

    /// <summary><c>topLeft</c>, <c>topRight</c>, <c>bottomLeft</c> or <c>bottomRight</c>; left out, the corner stays.</summary>
    public string? Corner { get; init; }
}
