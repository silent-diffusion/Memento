namespace Memento.Core.Bridge.Contracts;

/// <summary>A fix offered for a failed stage: <c>cpu</c>, <c>model:small</c>, <c>retry</c>.</summary>
public sealed record Remedy(string Id, string Label);
