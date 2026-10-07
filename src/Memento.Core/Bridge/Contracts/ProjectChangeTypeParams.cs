namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>project.changeType</c>: a built-in type or a custom one (1–40 characters).</summary>
public sealed record ProjectChangeTypeParams
{
    public required string RecordingId { get; init; }

    public required string Type { get; init; }
}
