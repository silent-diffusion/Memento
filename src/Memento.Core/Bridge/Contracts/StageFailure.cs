namespace Memento.Core.Bridge.Contracts;

/// <summary>Why a stage failed (DESIGN.md §17): what failed, what was kept, the most specific fix first.</summary>
public sealed record StageFailure(string Stage, string Message, string Kept, IReadOnlyList<Remedy> Remedies);
