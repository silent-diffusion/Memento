namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.copy</c>, for the "Copied 42 of 318 lines" confirmation.</summary>
/// <param name="Lines">The lines copied.</param>
/// <param name="TotalLines">The transcript's lines with words in them.</param>
/// <param name="Characters">The length of the copied text.</param>
public sealed record TranscriptCopyResult(int Lines, int TotalLines, int Characters);
