namespace Memento.AI.Payload;

/// <summary>A run of transcript lines that fits one request's budget, with its time range and speakers.</summary>
/// <param name="Index">0-based position among the chunks.</param>
/// <param name="Start">Start of the first line, seconds.</param>
/// <param name="End">End of the last line, seconds.</param>
/// <param name="SpeakerIds">Speakers in order of first appearance (<c>null</c> ids left out).</param>
/// <param name="Tokens">Tokens of <see cref="Text"/> for the counter used.</param>
/// <param name="Text">The lines, one per line, exactly as sent.</param>
/// <param name="EndsAt">The kind of boundary the chunk ends on.</param>
public sealed record TranscriptChunk(
    int Index,
    IReadOnlyList<TranscriptLine> Lines,
    double Start,
    double End,
    IReadOnlyList<string> SpeakerIds,
    int Tokens,
    string Text,
    ChunkBoundary EndsAt)
{
    public int FirstShortId => Lines[0].ShortId;

    public int LastShortId => Lines[^1].ShortId;
}
