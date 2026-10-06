namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters naming one recording: <c>{ recordingId }</c>.</summary>
public sealed record RecordingIdParams
{
    public required string RecordingId { get; init; }
}
