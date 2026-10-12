namespace Memento.Core.Bridge.Contracts;

/// <summary>One Library folder (2.1, <c>folders.list</c>).</summary>
/// <param name="Path">The folder's path, names joined by <c>/</c>: <c>Clients/Acme</c>. Its id in every folder method.</param>
/// <param name="Name">The last name of the path: <c>Acme</c>.</param>
/// <param name="Parent">The folder holding it; <c>null</c> at the top level.</param>
/// <param name="Count">Recordings filed directly in it.</param>
/// <param name="TotalCount">Recordings in it and in its folders, at any depth.</param>
public sealed record FolderInfo(string Path, string Name, string? Parent, int Count, int TotalCount);
