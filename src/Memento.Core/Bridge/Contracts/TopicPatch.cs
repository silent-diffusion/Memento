namespace Memento.Core.Bridge.Contracts;

/// <summary><c>Partial&lt;Topic&gt;</c> for <c>annotations.addTopic</c>; <see cref="Id"/> is ignored.</summary>
public sealed record TopicPatch
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public string? Origin { get; init; }
}
