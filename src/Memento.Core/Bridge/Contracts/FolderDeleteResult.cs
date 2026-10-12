namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>folders.delete</c> (2.1): what moved up into the parent. No recording is deleted.</summary>
/// <param name="RecordingsMoved">Recordings that were filed in the folder or its folders.</param>
/// <param name="FoldersMoved">Folders directly inside it, which now sit in its parent.</param>
public sealed record FolderDeleteResult(int RecordingsMoved, int FoldersMoved);
