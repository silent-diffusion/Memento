namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>project.deleteEstimate</c>: feeds the delete confirmation copy (DESIGN.md §17).</summary>
/// <param name="Items">What is removed, in words: "3 audio tracks", "the mix", "2 highlights", "processing history".</param>
public sealed record ProjectDeleteEstimateResult(string Title, long SizeBytes, IReadOnlyList<string> Items);
