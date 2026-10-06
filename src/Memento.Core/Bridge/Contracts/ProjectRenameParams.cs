namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>project.rename</c>.</summary>
public sealed record ProjectRenameParams
{
    public required string RecordingId { get; init; }

    public required string Title { get; init; }
}
