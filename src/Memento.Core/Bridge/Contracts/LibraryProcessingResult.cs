namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.processing</c>: the processing card, or <c>null</c> when nothing is running.</summary>
/// <param name="OthersCount">Other recordings processing at the same time ("+2 more").</param>
public sealed record LibraryProcessingResult(ProcessingCurrent? Current, int OthersCount);
