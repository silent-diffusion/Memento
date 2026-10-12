namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.moveToFolder</c> (2.1): how many recordings changed folder (ones already there count 0).</summary>
public sealed record LibraryMoveToFolderResult(int Moved);
