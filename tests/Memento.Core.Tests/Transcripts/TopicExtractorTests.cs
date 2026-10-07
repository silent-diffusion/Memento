using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Transcripts;

public sealed class TopicExtractorTests
{
    private static List<TranscriptSegment> Meeting()
    {
        var lines = new[]
        {
            "We need to talk about the marketing budget for Berlin.",
            "Yeah, I think the marketing budget is too small.",
            "Let's move on to the hiring plan.",
            "The hiring plan says two engineers in Berlin.",
            "Okay, so the marketing budget goes up and the hiring plan stays.",
            "Berlin office opens in March.",
            "Right, and the Berlin lease is signed.",
            "I think that's everything, thanks everyone.",
        };
        return lines.Select((l, i) => TranscriptFixtures.Segment($"s{i}", i * 40, (i * 40) + 30, l)).ToList();
    }

    [Fact]
    public void FindsRepeatedSubjectsAndPhrasesButNotFillerWords()
    {
        var topics = TopicExtractor.Extract(Meeting());

        Assert.Contains("Marketing budget", topics);
        Assert.Contains("Hiring plan", topics);
        Assert.Contains("Berlin", topics);
        Assert.DoesNotContain(topics, t => t.Equals("Think", StringComparison.OrdinalIgnoreCase) || t.Equals("Yeah", StringComparison.OrdinalIgnoreCase));
        Assert.True(topics.Count <= TopicExtractor.MaxTopics);
    }

    [Fact]
    public void AWordInsideAChosenPhraseIsNotRepeatedOnItsOwn()
    {
        var topics = TopicExtractor.Extract(Meeting());

        Assert.DoesNotContain("Budget", topics);
        Assert.DoesNotContain("Marketing", topics);
    }

    [Fact]
    public void AnEmptyOrTinyTranscriptHasNoTopics()
    {
        Assert.Empty(TopicExtractor.Extract([]));
        Assert.Empty(TopicExtractor.Extract([TranscriptFixtures.Segment("s1", 0, 2, "Hello there.")]));
    }
}
