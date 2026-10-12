namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>folders.move</c> (2.1).</summary>
public sealed record FolderMoveParams
{
    public required string Path { get; init; }

    /// <summary>The folder to move it into; <c>null</c> for the top level.</summary>
    public string? Parent { get; init; }
}
