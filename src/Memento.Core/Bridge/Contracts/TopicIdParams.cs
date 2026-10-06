namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>annotations.removeTopic</c>.</summary>
public sealed record TopicIdParams
{
    public required string RecordingId { get; init; }

    public required string TopicId { get; init; }
}
