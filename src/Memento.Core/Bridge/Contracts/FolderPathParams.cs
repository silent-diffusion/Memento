namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters naming one folder: <c>{ path }</c> (2.1, <c>folders.delete</c>).</summary>
public sealed record FolderPathParams
{
    public required string Path { get; init; }
}
