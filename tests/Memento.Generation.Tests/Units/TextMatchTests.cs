using Memento.Generation.Generation;

namespace Memento.Generation.Tests.Units;

/// <summary>Quotes must be the transcript's words: whole, or pieces of it in order.</summary>
public sealed class TextMatchTests
{
    private const string Line = "We ship 3.2 on Thursday, November twelfth, unless QA finds a blocker this week.";

    [Theory]
    [InlineData("We ship 3.2 on Thursday", true)]
    [InlineData("we ship 3.2 ON thursday, november", true)]
    [InlineData("We ship 3.2 … unless QA finds a blocker", true)]
    [InlineData("We ship 3.2... finds a blocker", true)]
    [InlineData("unless QA finds … We ship 3.2", false)]
    [InlineData("We … ship … blocker", false)]
    [InlineData("We ship 3.2 … QA … this week", false)]
    [InlineData("We ship 3.2 … We ship 3.2", false)]
    [InlineData("ship on Thursday", false)]
    [InlineData("…", false)]
    public void PiecesMatchInOrderAndEachHasEnoughWords(string quote, bool expected) =>
        Assert.Equal(expected, TextMatch.QuoteIn(quote, Line));

    [Fact]
    public void AQuoteMayRepeatWordsTheLineRepeats() =>
        Assert.True(TextMatch.QuoteIn("we ship it … we ship it", "We ship it, and then we ship it again."));
}
