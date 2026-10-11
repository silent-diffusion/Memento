namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>folders.create</c> (2.1).</summary>
public sealed record FolderCreateParams
{
    /// <summary>The folder to create it in; <c>null</c> for the top level.</summary>
    public string? Parent { get; init; }

    public required string Name { get; init; }
}
