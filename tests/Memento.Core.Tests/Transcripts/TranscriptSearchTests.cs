using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Transcripts;

public sealed class TranscriptSearchTests
{
    private static readonly IReadOnlyList<Memento.Core.Bridge.Contracts.TranscriptSegment> Segments =
    [
        TranscriptFixtures.Segment("s1", 0, 2, "Let me explain the plan."),
        TranscriptFixtures.Segment("s2", 2, 4, "Planning starts on Monday."),
        TranscriptFixtures.Segment("s3", 4, 6, "Café opening is in Q3; the PLAN, again."),
    ];

    [Fact]
    public void MatchesStartAtAWordBoundaryAndIgnoreCase()
    {
        var matches = TranscriptSearch.Find(Segments, "plan");

        Assert.Equal(["s1", "s2", "s3"], matches.Select(m => m.SegmentId));
        Assert.Equal(2, matches[1].Start);
    }

    [Fact]
    public void AWordInsideAnotherWordIsNotAMatch()
    {
        Assert.Empty(TranscriptSearch.Find(Segments, "lain"));
        Assert.Empty(TranscriptSearch.Find(Segments, "xplain"));
    }

    [Fact]
    public void AccentsAreIgnored()
    {
        Assert.Equal(["s3"], TranscriptSearch.Find(Segments, "cafe").Select(m => m.SegmentId));
    }

    [Fact]
    public void ABlankQueryFindsNothing()
    {
        Assert.Empty(TranscriptSearch.Find(Segments, "  "));
    }

    [Fact]
    public void LongTextIsShortenedAroundTheMatch()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 40)) + " budget " + string.Join(' ', Enumerable.Repeat("more", 40));

        var snippet = TranscriptSearch.Snippet(text, text.IndexOf("budget", StringComparison.Ordinal), 6);

        Assert.StartsWith("…", snippet, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
        Assert.Contains("budget", snippet, StringComparison.Ordinal);
        Assert.True(snippet.Length < 140);
        Assert.DoesNotContain("wor ", snippet, StringComparison.Ordinal);
    }
}
