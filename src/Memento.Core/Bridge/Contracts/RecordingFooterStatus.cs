namespace Memento.Core.Bridge.Contracts;

/// <summary>The recording part of the status footer.</summary>
/// <param name="LostSource">Name of a source that stopped mid-recording, for the footer warning.</param>
public sealed record RecordingFooterStatus(bool Active, DateTimeOffset? LastCheckpointAt, string? LostSource)
{
    public static RecordingFooterStatus Idle { get; } = new(false, null, null);
}
