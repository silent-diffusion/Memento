namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>library.moveToFolder</c> (2.1).</summary>
public sealed record LibraryMoveToFolderParams
{
    public required IReadOnlyList<string> RecordingIds { get; init; }

    /// <summary>The folder to file them in; <c>null</c> for the root.</summary>
    public string? Folder { get; init; }
}
