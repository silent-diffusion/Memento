namespace Memento.Core.Bridge.Contracts;

/// <summary>The chosen folder, or <c>null</c> when the user cancelled.</summary>
public sealed record PickFolderResult(string? Path);
