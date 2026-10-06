namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>annotations.addTopic</c>.</summary>
public sealed record TopicParams
{
    public required string RecordingId { get; init; }

    public required TopicPatch Topic { get; init; }
}
