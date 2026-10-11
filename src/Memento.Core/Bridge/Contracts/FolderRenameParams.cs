namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>folders.rename</c> (2.1).</summary>
public sealed record FolderRenameParams
{
    public required string Path { get; init; }

    public required string Name { get; init; }
}
