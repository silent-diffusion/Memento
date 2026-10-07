using System.Text;
using Memento.AI.Payload;

namespace Memento.Generation.Generation;

/// <summary>
/// The transcript as the pipeline cites it: one entry per short id (the parts of a split segment joined), with its
/// segment id, start time, speaker and text, and the excerpts the verifier reads.
/// </summary>
public sealed class TranscriptIndex
{
    private readonly SortedDictionary<int, Entry> _lines = [];

    public TranscriptIndex(IReadOnlyList<TranscriptLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var group in lines.GroupBy(l => l.ShortId))
        {
            var first = group.OrderBy(l => l.Part).First();
            _lines[group.Key] = new Entry(group.Key, first.SegmentId, first.Start, first.End, first.Speaker, string.Join(' ', group.OrderBy(l => l.Part).Select(l => l.Text)));
        }
    }

    public IReadOnlyCollection<int> Ids => _lines.Keys;

    public Entry? Find(int? shortId) => shortId is { } id && _lines.TryGetValue(id, out var entry) ? entry : null;

    /// <summary>The lines from <paramref name="before"/> lines before the cited one to <paramref name="after"/> after it.</summary>
    public IReadOnlyList<Entry> Span(int shortId, int before = 2, int after = 2) =>
        _lines.Values.Where(e => e.ShortId >= shortId - before && e.ShortId <= shortId + after).ToList();

    /// <summary>The span as the verifier reads it: <c>[12] Speaker: words</c> per line.</summary>
    public string Excerpt(int shortId, int before = 2, int after = 2)
    {
        var builder = new StringBuilder();
        foreach (var entry in Span(shortId, before, after))
        {
            builder.Append('[').Append(entry.ShortId).Append("] ").Append(entry.Speaker).Append(": ").Append(entry.Text).Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>One cited line.</summary>
    public sealed record Entry(int ShortId, string SegmentId, double Start, double End, string Speaker, string Text);
}
