namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>folders.list</c> (2.1): every folder, sorted by path, and the recordings at the root.</summary>
public sealed record FoldersListResult(IReadOnlyList<FolderInfo> Folders, int RootCount);
