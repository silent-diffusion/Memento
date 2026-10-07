using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Transcripts;

public sealed class WordAlignerTests
{
    [Fact]
    public void SpreadsTheSpanInProportionToWordLength()
    {
        var words = WordAligner.Realign("a bbb", 10, 16);

        Assert.Equal(["a", "bbb"], words.Select(w => w.W));
        Assert.Equal(10, words[0].S);
        Assert.Equal(12, words[0].E);
        Assert.Equal(12, words[1].S);
        Assert.Equal(16, words[1].E);
        Assert.All(words, w => Assert.Equal(1, w.C));
    }

    [Fact]
    public void ExtraWhitespaceAndAnEmptyTextAreHandled()
    {
        Assert.Equal(["one", "two"], WordAligner.Realign("  one \n two  ", 0, 1).Select(w => w.W));
        Assert.Empty(WordAligner.Realign("   ", 0, 1));
    }

    [Fact]
    public void AZeroLengthSpanPutsEveryWordAtTheStart()
    {
        var words = WordAligner.Realign("x y", 5, 5);

        Assert.All(words, w => { Assert.Equal(5, w.S); Assert.Equal(5, w.E); });
    }
}
