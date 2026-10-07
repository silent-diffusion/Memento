namespace Memento.Core.Bridge.Contracts;

/// <summary>One word of a transcript segment. Times are seconds on the recording timeline.</summary>
/// <param name="W">The word as spoken, with its punctuation.</param>
/// <param name="S">Start, seconds.</param>
/// <param name="E">End, seconds.</param>
/// <param name="C">Confidence 0..1 (the lowest of its tokens; 1 for words the user typed).</param>
public sealed record TranscriptWord(string W, double S, double E, double C);
