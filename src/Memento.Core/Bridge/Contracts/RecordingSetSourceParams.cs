namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>recording.setSource</c>: start or end one track.</summary>
public sealed record RecordingSetSourceParams
{
    public required string SessionId { get; init; }

    public required string SourceId { get; init; }

    public required bool Enabled { get; init; }
}
