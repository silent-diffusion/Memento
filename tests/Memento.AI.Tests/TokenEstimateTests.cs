namespace Memento.AI.Tests;

public sealed class TokenEstimateTests
{
    private const string Prose =
        "We agreed to ship release 3.2 on Thursday, November 12. Recurring invoice templates move into 3.3, and Luis " +
        "will cut the branch on Friday. The pricing page drops the annual-only plan in favour of a monthly/annual toggle.";

    [Fact]
    public void EmptyTextIsZeroAndMoreTextIsNeverFewerTokens()
    {
        var counter = EstimatingTokenCounter.Generic;

        Assert.Equal(0, counter.Count(string.Empty));
        Assert.True(counter.Count(Prose + Prose) >= counter.Count(Prose));
        Assert.True(counter.Count("a") >= 1);
    }

    [Fact]
    public void EnglishProseLandsNearFourCharactersPerTokenBeforeTheFactor()
    {
        var raw = new EstimatingTokenCounter(1.0).Count(Prose);
        var charsPerToken = Prose.Length / (double)raw;

        // Modern BPE tokenizers give 3.5-4.5 characters per token on English prose; the estimate must not undercount.
        Assert.InRange(charsPerToken, 3.0, 4.2);
    }

    [Fact]
    public void CloudFactorsErrHighAndClaudeHighest()
    {
        var generic = new EstimatingTokenCounter(1.0).Count(Prose);

        Assert.True(EstimatingTokenCounter.Claude.Count(Prose) > EstimatingTokenCounter.OpenAi.Count(Prose));
        Assert.True(EstimatingTokenCounter.OpenAi.Count(Prose) > generic);
        Assert.False(EstimatingTokenCounter.Claude.IsExact);
    }

    [Theory]
    [InlineData("1234567", 3)]
    [InlineData("会議の議事録", 6)]
    [InlineData("a, b; c!", 6)]
    [InlineData("line\nline", 3)]
    [InlineData("internationalisation", 5)]
    public void CountsDigitsUnspacedScriptsPunctuationLinesAndLongWords(string text, int expected) =>
        Assert.Equal(expected, new EstimatingTokenCounter(1.0).Count(text));
}
