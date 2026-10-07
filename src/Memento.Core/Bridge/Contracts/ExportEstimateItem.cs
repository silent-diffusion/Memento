namespace Memento.Core.Bridge.Contracts;

/// <summary>One file an export would write, with its (estimated) size.</summary>
public sealed record ExportEstimateItem(string Name, long Bytes);
