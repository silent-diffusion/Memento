namespace Memento.Core.Bridge.Contracts;

/// <summary>One item of a parsed agenda, before it is applied (BRIDGE.md M3).</summary>
/// <param name="UncertainReason">Why the parser is unsure, in words the UI shows as is.</param>
/// <param name="Level">0 for a top-level item, 1 for its sub-items, and so on.</param>
/// <param name="Location">Where the item was found, e.g. "page 2, line 14"; <c>null</c> when it does not apply.</param>
public sealed record AgendaParsedItem(string Text, bool Uncertain, string? UncertainReason, int Level, string? Location);
