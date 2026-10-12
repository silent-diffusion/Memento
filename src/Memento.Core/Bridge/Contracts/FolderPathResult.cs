namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>folders.create</c>, <c>folders.rename</c> and <c>folders.move</c> (2.1): the folder's path now.</summary>
public sealed record FolderPathResult(string Path);
