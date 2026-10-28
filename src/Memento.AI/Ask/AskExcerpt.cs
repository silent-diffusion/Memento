namespace Memento.AI.Ask;

/// <summary>One transcript section an Ask answer was given (2.9.0): its lines, times and why it was picked.</summary>
/// <param name="Index">The section's position among all sections, from 0.</param>
/// <param name="FirstLine">Short id (line number) of its first line.</param>
/// <param name="LastLine">Short id of its last line.</param>
/// <param name="Start">Start of its first line, seconds.</param>
/// <param name="End">End of its last line, seconds.</param>
/// <param name="Title">The chapter it falls in, when the recording has chapters.</param>
/// <param name="Matched">It matched the question's words (otherwise it is a neighbour of one that did, or the opening when nothing matched).</param>
public sealed record AskExcerpt(int Index, int FirstLine, int LastLine, double Start, double End, string? Title, bool Matched);
