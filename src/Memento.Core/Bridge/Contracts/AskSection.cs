namespace Memento.Core.Bridge.Contracts;

/// <summary>One transcript section an Ask answer was given, when the recording did not fit whole (2.9.0).</summary>
/// <param name="Number">1-based among all sections.</param>
/// <param name="FirstLine">Its first line number.</param>
/// <param name="LastLine">Its last line number.</param>
/// <param name="Start">Seconds.</param>
/// <param name="End">Seconds.</param>
/// <param name="Title">The chapter it falls in, or <c>null</c>.</param>
/// <param name="Matched">It matched the question's words; otherwise it is a neighbour of one that did, or the opening.</param>
public sealed record AskSection(int Number, int FirstLine, int LastLine, double Start, double End, string? Title, bool Matched);