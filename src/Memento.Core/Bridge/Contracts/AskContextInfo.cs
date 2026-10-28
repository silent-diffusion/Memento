namespace Memento.Core.Bridge.Contracts;

/// <summary>What an Ask answer was given (2.9.0): the whole recording, or the sections the local search picked.</summary>
/// <param name="Mode"><c>whole</c> or <c>sections</c>.</param>
/// <param name="Sections">With <c>sections</c>: the sections sent, in transcript order.</param>
/// <param name="SectionCount">With <c>sections</c>: how many sections the transcript was cut into.</param>
/// <param name="TotalLines">Lines in the transcript.</param>
/// <param name="Inputs">What went along besides the transcript ("Recording details", "Agenda"…), by name.</param>
/// <param name="AnyMatched">With <c>sections</c>: some section shared words with the question (otherwise the opening was used).</param>
/// <param name="Tokens">The material's size for the provider's counter.</param>
public sealed record AskContextInfo(string Mode, IReadOnlyList<AskSection> Sections, int SectionCount, int TotalLines, IReadOnlyList<string> Inputs, bool AnyMatched, int Tokens);