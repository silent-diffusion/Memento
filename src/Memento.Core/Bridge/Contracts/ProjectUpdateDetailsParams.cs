namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>project.updateDetails</c>.</summary>
public sealed record ProjectUpdateDetailsParams
{
    public required string RecordingId { get; init; }

    public required RecordingDetailsPatch Details { get; init; }
}
