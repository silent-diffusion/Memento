namespace Memento.AI.Ask;

/// <summary>
/// What an Ask question is answered from (2.9.0): the whole recording when it fits the model's budget, else the
/// sections the local search picked. <see cref="Text"/> is exactly what goes into the request.
/// </summary>
/// <param name="Whole">The whole transcript (and the other inputs) is sent; <see cref="Excerpts"/> is empty.</param>
/// <param name="Text">The recording material as it goes into the question's message.</param>
/// <param name="Tokens">Tokens of <see cref="Text"/> for the provider's counter.</param>
/// <param name="Excerpts">With retrieval: the sections used, in transcript order.</param>
/// <param name="SectionCount">With retrieval: how many sections the transcript was cut into.</param>
/// <param name="TotalLines">Lines in the whole transcript.</param>
/// <param name="ContextIncluded">The details, agenda, participants, outline and notes went along.</param>
/// <param name="AnyMatched">With retrieval: some section matched the question's words.</param>
public sealed record AskContext(
    bool Whole,
    string Text,
    int Tokens,
    IReadOnlyList<AskExcerpt> Excerpts,
    int SectionCount,
    int TotalLines,
    bool ContextIncluded,
    bool AnyMatched)
{
    /// <summary>Whether line <paramref name="shortId"/> is in what was sent.</summary>
    public bool Sent(int shortId) => Whole ? shortId >= 1 && shortId <= TotalLines : Excerpts.Any(e => shortId >= e.FirstLine && shortId <= e.LastLine);
}
