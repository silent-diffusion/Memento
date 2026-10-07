namespace Memento.Documents.Model.Blocks;

/// <summary>
/// The Full transcript module's content (ARCHITECTURE.md §8): every segment with its speaker, time and text, and optional
/// chapter headings placed before the first segment at or after each chapter's time. Placed as data; no AI is involved.
/// </summary>
public sealed record TranscriptBlock : Block
{
    public override string Type => BlockTypes.Transcript;

    public IReadOnlyList<TranscriptLine> Segments { get; init; } = [];

    public IReadOnlyList<TranscriptChapter> Chapters { get; init; } = [];

    /// <summary>Segments and chapter headings in reading order: each chapter comes before the first segment at or after its time.</summary>
    public IEnumerable<object> Interleaved()
    {
        var chapters = Chapters.OrderBy(c => c.T).ToList();
        var next = 0;
        foreach (var segment in Segments)
        {
            while (next < chapters.Count && chapters[next].T <= segment.T)
            {
                yield return chapters[next++];
            }

            yield return segment;
        }

        while (next < chapters.Count)
        {
            yield return chapters[next++];
        }
    }
}
