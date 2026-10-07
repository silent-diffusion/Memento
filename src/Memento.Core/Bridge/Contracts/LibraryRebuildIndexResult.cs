namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.rebuildIndex</c>: how many recordings were indexed.</summary>
public sealed record LibraryRebuildIndexResult(int Recordings);
