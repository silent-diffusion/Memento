namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>recovery.list</c>: recordings repaired at launch that the user has not dismissed.</summary>
public sealed record RecoveryListResult(IReadOnlyList<RecoveryItem> Items);
