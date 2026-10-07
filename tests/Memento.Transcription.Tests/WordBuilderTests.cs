using Memento.Transcription.Words;

namespace Memento.Transcription.Tests;

public sealed class WordBuilderTests
{
    [Fact]
    public void ATokenStartingWithASpaceStartsAWordAndOthersContinueIt()
    {
        var segments = TokenFixtures.Load("tokens-after-twenty-years.json");

        var first = WordBuilder.Build(segments[0], 0);
        var second = WordBuilder.Build(segments[1], 0);

        Assert.Equal(["After", "Twenty", "Years", "by", "O.", "Henry."], first.Select(w => w.W));
        Assert.Equal(["The", "policeman", "on", "the", "beat", "moved", "up", "the", "avenue", "impressively."], second.Select(w => w.W));
    }

    [Fact]
    public void AWordSpansItsTokensAndTakesTheLowestProbability()
    {
        var words = WordBuilder.Build(TokenFixtures.Load("tokens-after-twenty-years.json")[1], 0);

        var policeman = words[1];
        Assert.Equal(19.10, policeman.S);
        Assert.Equal(19.90, policeman.E);
        Assert.Equal(0.59, policeman.C);
        var last = words[^1];
        Assert.Equal(22.30, last.S);
        Assert.Equal(23.72, last.E);
        Assert.Equal(0.80, last.C);
    }

    [Fact]
    public void SpecialTokensCarryNoWords()
    {
        var segments = TokenFixtures.Load("tokens-after-twenty-years.json");

        Assert.DoesNotContain(WordBuilder.Build(segments[0], 0), w => w.W.Contains('[', StringComparison.Ordinal));
        Assert.DoesNotContain(WordBuilder.Build(segments[1], 0), w => w.W.Contains("<|", StringComparison.Ordinal));
        Assert.Empty(WordBuilder.Build(TokenFixtures.Load("tokens-odd-times.json")[1], 0));
    }

    [Fact]
    public void TimesAreClampedToTheSegmentAndShiftedToTheTimeline()
    {
        var words = WordBuilder.Build(TokenFixtures.Load("tokens-odd-times.json")[0], 600);

        Assert.Equal(["Okay,", "yes."], words.Select(w => w.W));
        Assert.Equal(605.0, words[0].S);
        Assert.Equal(605.2, words[0].E);
        Assert.True(words[1].E >= words[1].S);
        Assert.True(words[1].E <= 606.0);
        Assert.Equal(0.40, words[0].C);
    }

    [Fact]
    public void TheSegmentConfidenceIsItsLowestWord()
    {
        var segment = WordBuilder.ToSegment(TokenFixtures.Load("tokens-after-twenty-years.json")[0], 10, keepWords: true);

        Assert.Equal(0.364, segment.Confidence);
        Assert.Equal("After Twenty Years by O. Henry.", segment.Text);
        Assert.Equal(10, segment.Start);
        Assert.Equal(13.62, segment.End);
        Assert.Equal(6, segment.Words.Count);
    }

    [Fact]
    public void WithoutWordTimestampsTheWordsAreDroppedButStillSetTheConfidence()
    {
        var segment = WordBuilder.ToSegment(TokenFixtures.Load("tokens-after-twenty-years.json")[0], 0, keepWords: false);

        Assert.Empty(segment.Words);
        Assert.Equal(0.364, segment.Confidence);
    }

    [Fact]
    public void ASegmentWithoutTokensUsesTheEngineMinimum()
    {
        var segment = WordBuilder.ToSegment(new RawSegment(1, 2, " Hm.", 0.42, []), 0, keepWords: true);

        Assert.Equal(0.42, segment.Confidence);
        Assert.Empty(segment.Words);
    }
}
