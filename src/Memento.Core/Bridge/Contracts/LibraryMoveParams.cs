namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>library.move</c>: the folder the library moves to (empty or new).</summary>
public sealed record LibraryMoveParams
{
    public required string NewPath { get; init; }
}
