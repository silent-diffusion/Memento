using Memento.AI.Payload;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Payload;

public sealed class TranscriptChunkerTests
{
    private static readonly EstimatingTokenCounter Counter = EstimatingTokenCounter.Generic;

    [Theory]
    [InlineData(3000, 1500)]
    [InlineData(3000, 3000)]
    [InlineData(10000, 1500)]
    [InlineData(400, 800)]
    public void ALongTranscriptFitsTheBudgetAndKeepsEverySegmentOnceInOrder(int segments, int budget)
    {
        var (lines, chapters, topics) = Lines(segments);

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(budget, Counter), chapters, topics);

        Assert.All(chunks, c => Assert.True(c.Tokens <= budget, $"chunk {c.Index} has {c.Tokens} tokens"));
        Assert.Equal(lines.Select(l => l.SegmentId), chunks.SelectMany(c => c.Lines).Select(l => l.SegmentId));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
        Assert.Equal(ChunkBoundary.End, chunks[^1].EndsAt);
        var total = lines.Sum(l => Counter.Count(l.Rendered) + 1);
        Assert.InRange(chunks.Count, (int)Math.Ceiling(total / (double)budget), (int)Math.Ceiling(total / (double)budget * 2) + 1);
    }

    [Fact]
    public void ChunksEndAtSpeakerTurnsOrStrongerAndAreWellFilled()
    {
        var (lines, chapters, topics) = Lines(5000);
        const int budget = 1500;

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(budget, Counter), chapters, topics);

        var inner = chunks.Take(chunks.Count - 1).ToList();
        Assert.True(inner.Count(c => c.EndsAt >= ChunkBoundary.SpeakerTurn) >= 0.95 * inner.Count, "chunks should end where the speaker changes");
        var unforced = inner.Where(c => c.EndsAt != ChunkBoundary.Chapter).ToList();
        Assert.True(unforced.Average(c => c.Tokens) >= 0.75 * budget, $"average fill {unforced.Average(c => c.Tokens)}");
    }

    [Fact]
    public void ANewChapterStartsANewChunkUnlessTheChunkWouldBeASliver()
    {
        var (lines, chapters, topics) = Lines(4000, chapterEvery: 150);
        const int budget = 1500;

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(budget, Counter), chapters, topics);

        foreach (var chunk in chunks)
        {
            for (var i = 1; i < chunk.Lines.Count; i++)
            {
                if (chapters.Any(c => c.Start > chunk.Lines[i - 1].Start && c.Start <= chunk.Lines[i].Start))
                {
                    var before = chunk.Lines.Take(i).Sum(l => Counter.Count(l.Rendered) + 1);
                    Assert.True(before < 0.2 * budget, $"chunk {chunk.Index} crosses a chapter after {before} tokens");
                }
            }
        }

        Assert.Contains(chunks, c => c.EndsAt == ChunkBoundary.Chapter);
    }

    [Fact]
    public void ATopicStartIsPreferredOverASpeakerTurnWhenTheChunkIsFullEnough()
    {
        // Speaker turns everywhere, one topic start at 75% of the budget.
        var segments = Enumerable.Range(0, 40).Select(i => new PayloadSegment($"s{i}", i * 10, (i * 10) + 8, i % 2 == 0 ? "spk1" : "spk2", "one two three four five six seven eight nine ten eleven twelve.")).ToList();
        var lines = PayloadComposer.RenderLines(segments, SyntheticTranscript.Speakers(2));
        var perLine = Counter.Count(lines[0].Rendered) + 1;
        var budget = perLine * 20;
        var topics = new[] { new PayloadMarker(150, "Pricing") };

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(budget, Counter), topics: topics);

        Assert.Equal(15, chunks[0].Lines.Count);
        Assert.Equal(ChunkBoundary.Topic, chunks[0].EndsAt);
    }

    [Fact]
    public void ChunksCarryTheirTimeRangeAndSpeakers()
    {
        var (lines, chapters, topics) = Lines(1200);

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(1000, Counter), chapters, topics);

        Assert.All(chunks, c =>
        {
            Assert.Equal(c.Lines[0].Start, c.Start);
            Assert.Equal(c.Lines.Max(l => l.End), c.End);
            Assert.Equal(c.Lines.Select(l => l.SpeakerId!).Distinct().ToList(), c.SpeakerIds);
            Assert.Equal(string.Join('\n', c.Lines.Select(l => l.Rendered)), c.Text);
            Assert.Equal(c.Lines[0].ShortId, c.FirstShortId);
        });
        Assert.True(chunks.Zip(chunks.Skip(1)).All(p => p.First.End <= p.Second.Start));
    }

    [Fact]
    public void ASegmentLongerThanTheBudgetIsSplitIntoPartsThatKeepItsShortId()
    {
        var words = string.Join(' ', Enumerable.Range(0, 3000).Select(i => i % 17 == 16 ? "end." : "word"));
        var segments = new[]
        {
            new PayloadSegment("s1", 0, 5, "spk1", "Short opening."),
            new PayloadSegment("s2", 6, 900, "spk2", words),
            new PayloadSegment("s3", 901, 905, "spk1", "Short close."),
        };
        var lines = PayloadComposer.RenderLines(segments, SyntheticTranscript.Speakers(2));
        const int budget = 300;

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(budget, Counter));

        Assert.All(chunks, c => Assert.True(c.Tokens <= budget));
        var parts = chunks.SelectMany(c => c.Lines).Where(l => l.SegmentId == "s2").ToList();
        Assert.True(parts.Count > 5);
        Assert.All(parts, p => Assert.Equal(2, p.ShortId));
        Assert.Equal(Enumerable.Range(1, parts.Count), parts.Select(p => p.Part));
        Assert.Equal(words, string.Join(' ', parts.Select(p => p.Text)));
        Assert.Equal(["s1", "s2", "s3"], chunks.SelectMany(c => c.Lines).Select(l => l.SegmentId).Distinct());
    }

    [Fact]
    public void AnUnbrokenBlobIsCutByCharacters()
    {
        var blob = new string('x', 5000);
        var lines = PayloadComposer.RenderLines([new PayloadSegment("s1", 0, 1, "spk1", blob)], SyntheticTranscript.Speakers(1));

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(200, Counter));

        Assert.All(chunks, c => Assert.True(c.Tokens <= 200));
        Assert.Equal(blob, string.Concat(chunks.SelectMany(c => c.Lines).Select(l => l.Text)));
    }

    [Fact]
    public void AnEmptyTranscriptHasNoChunks() =>
        Assert.Empty(TranscriptChunker.Chunk([], new ChunkOptions(1500, Counter)));

    [Fact]
    public void ChunkingIsDeterministic()
    {
        var (lines, chapters, topics) = Lines(2000);

        var a = TranscriptChunker.Chunk(lines, new ChunkOptions(1500, Counter), chapters, topics);
        var b = TranscriptChunker.Chunk(lines, new ChunkOptions(1500, Counter), chapters, topics);

        Assert.Equal(a.Select(c => c.Text), b.Select(c => c.Text));
    }

    private static (IReadOnlyList<TranscriptLine> Lines, IReadOnlyList<PayloadMarker> Chapters, IReadOnlyList<PayloadMarker> Topics) Lines(int segments, int chapterEvery = 400)
    {
        var (list, chapters, topics) = SyntheticTranscript.Create(segments, chapterEvery: chapterEvery);
        return (PayloadComposer.RenderLines(list, SyntheticTranscript.Speakers(4)), chapters, topics);
    }
}
