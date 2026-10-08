using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.M3;

/// <summary>
/// The readable transcript formatter (<see cref="TranscriptText"/>) with every combination of timestamps, speakers and
/// layout, for Markdown and plain text: the export files and the clipboard both come from it.
/// </summary>
public sealed class TranscriptTextTests
{
    private const string MarkdownHeading = "# Weekly sync\n\n2026-10-06 10:00 · 1:02:13";
    private const string PlainHeading = "Weekly sync\n2026-10-06 10:00 · 1:02:13";
    private const string SpeakersPart = " · Speakers: Speaker 1, Speaker 2";

    private static TranscriptDocument Sample() => TranscriptFixtures.Document(
        TranscriptFixtures.Segment("s1", 0.4, 2, "Hello  there.", "spk1"),
        TranscriptFixtures.Segment("s2", 3, 4, "Second line.", "spk1"),
        TranscriptFixtures.Segment("s3", 65, 66, "Reply [x].", "spk2"),
        TranscriptFixtures.Segment("s4", 66, 67, "   ", "spk2"),
        TranscriptFixtures.Segment("s5", 70.9, 71, "No speaker."));

    private static RecordingSummary Summary() =>
        new("r1", "Weekly sync", "meeting", new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1)), 3_733_000, 2, false, [], [], false, "ready", 0);

    private static string Crlf(string text) => text.Replace("\n", "\r\n", StringComparison.Ordinal);

    /// <summary>Every combination: format, timestamps, speakers, layout, and the body after the heading line.</summary>
    public static TheoryData<string, bool, bool, string, string> Combinations()
    {
        var data = new TheoryData<string, bool, bool, string, string>();
        foreach (var layout in new[] { "auto", "turns" })
        {
            data.Add("markdown", true, true, layout, "\n**Speaker 1:** [0:00:00] Hello there. [0:00:03] Second line.\n\n**Speaker 2:** [0:01:05] Reply \\[x\\].\n\n[0:01:10] No speaker.\n");
            data.Add("markdown", true, false, layout, "\n[0:00:00] Hello there. [0:00:03] Second line.\n\n[0:01:05] Reply \\[x\\].\n\n[0:01:10] No speaker.\n");
            data.Add("markdown", false, true, layout, "\n**Speaker 1:** Hello there. Second line.\n\n**Speaker 2:** Reply \\[x\\].\n\nNo speaker.\n");
            data.Add("markdown", false, false, layout, "\nHello there. Second line.\n\nReply \\[x\\].\n\nNo speaker.\n");
        }

        data.Add("markdown", true, true, "lines", "\n**Speaker 1:** [0:00:00] Hello there.\n\n**Speaker 1:** [0:00:03] Second line.\n\n**Speaker 2:** [0:01:05] Reply \\[x\\].\n\n[0:01:10] No speaker.\n");
        data.Add("markdown", true, false, "lines", "\n[0:00:00] Hello there.\n\n[0:00:03] Second line.\n\n[0:01:05] Reply \\[x\\].\n\n[0:01:10] No speaker.\n");
        data.Add("markdown", false, true, "lines", "\n**Speaker 1:** Hello there.\n\n**Speaker 1:** Second line.\n\n**Speaker 2:** Reply \\[x\\].\n\nNo speaker.\n");
        data.Add("markdown", false, false, "lines", "\nHello there.\n\nSecond line.\n\nReply \\[x\\].\n\nNo speaker.\n");

        foreach (var layout in new[] { "auto", "lines" })
        {
            data.Add("text", true, true, layout, "[0:00:00] Speaker 1: Hello there.\n[0:00:03] Speaker 1: Second line.\n[0:01:05] Speaker 2: Reply [x].\n[0:01:10] No speaker.\n");
            data.Add("text", true, false, layout, "[0:00:00] Hello there.\n[0:00:03] Second line.\n[0:01:05] Reply [x].\n[0:01:10] No speaker.\n");
            data.Add("text", false, true, layout, "Speaker 1: Hello there.\nSpeaker 1: Second line.\nSpeaker 2: Reply [x].\nNo speaker.\n");
            data.Add("text", false, false, layout, "Hello there.\nSecond line.\nReply [x].\nNo speaker.\n");
        }

        data.Add("text", true, true, "turns", "\nSpeaker 1: [0:00:00] Hello there. [0:00:03] Second line.\n\nSpeaker 2: [0:01:05] Reply [x].\n\n[0:01:10] No speaker.\n");
        data.Add("text", true, false, "turns", "\n[0:00:00] Hello there. [0:00:03] Second line.\n\n[0:01:05] Reply [x].\n\n[0:01:10] No speaker.\n");
        data.Add("text", false, true, "turns", "\nSpeaker 1: Hello there. Second line.\n\nSpeaker 2: Reply [x].\n\nNo speaker.\n");
        data.Add("text", false, false, "turns", "\nHello there. Second line.\n\nReply [x].\n\nNo speaker.\n");
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void EveryCombinationOfTimestampsSpeakersAndLayout(string format, bool timestamps, bool speakers, string layout, string body)
    {
        var options = new TranscriptTextOptions { Timestamps = timestamps, Speakers = speakers, Layout = layout };

        var text = TranscriptText.Format(format, Sample(), Summary(), options);

        var heading = (format == "markdown" ? MarkdownHeading : PlainHeading) + (speakers ? SpeakersPart : string.Empty) + "\n";
        Assert.Equal(Crlf(heading + body), text);
    }

    [Fact]
    public void TheDefaultsWriteWhatMementoAlwaysWrote()
    {
        var transcript = Sample();

        Assert.Equal(TranscriptText.Markdown(transcript, Summary()), TranscriptText.Format("markdown", transcript, Summary(), new TranscriptTextOptions()));
        Assert.Equal(TranscriptText.Plain(transcript, Summary()), TranscriptText.Format("text", transcript, Summary(), null));
        Assert.Equal(Crlf(MarkdownHeading + SpeakersPart + "\n\n**Speaker 1:** [0:00:00] Hello there. [0:00:03] Second line.\n\n**Speaker 2:** [0:01:05] Reply \\[x\\].\n\n[0:01:10] No speaker.\n"), TranscriptText.Markdown(transcript, Summary()));
        Assert.Equal(Crlf(PlainHeading + SpeakersPart + "\n[0:00:00] Speaker 1: Hello there.\n[0:00:03] Speaker 1: Second line.\n[0:01:05] Speaker 2: Reply [x].\n[0:01:10] No speaker.\n"), TranscriptText.Plain(transcript, Summary()));
    }

    [Fact]
    public void OnlyTheChosenLinesAreWrittenAndTheHeadingNamesTheirSpeakers()
    {
        var only = new HashSet<string>(["s2", "s5"], StringComparer.Ordinal);

        var text = TranscriptText.Plain(Sample(), Summary(), only: only);
        var markdown = TranscriptText.Markdown(Sample(), Summary(), new TranscriptTextOptions { Layout = "turns" }, only);

        Assert.Equal(Crlf(PlainHeading + " · Speakers: Speaker 1\n[0:00:03] Speaker 1: Second line.\n[0:01:10] No speaker.\n"), text);
        Assert.Equal(Crlf(MarkdownHeading + " · Speakers: Speaker 1\n\n**Speaker 1:** [0:00:03] Second line.\n\n[0:01:10] No speaker.\n"), markdown);
        Assert.Equal(["s2", "s5"], TranscriptText.Selected(Sample(), only).Select(s => s.Id));
    }

    [Fact]
    public void NoLinesLeavesTheHeadingAlone()
    {
        var none = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal(Crlf(MarkdownHeading + "\n"), TranscriptText.Markdown(Sample(), Summary(), only: none));
        Assert.Equal(Crlf(PlainHeading + "\n"), TranscriptText.Plain(Sample(), Summary(), new TranscriptTextOptions { Layout = "turns" }, none));
    }

    [Theory]
    [InlineData("markdown", "auto", "turns")]
    [InlineData("text", "auto", "lines")]
    [InlineData("markdown", "lines", "lines")]
    [InlineData("text", "turns", "turns")]
    public void AutoIsEachFormatsOwnLayout(string format, string layout, string expected)
    {
        Assert.Equal(expected, TranscriptText.LayoutFor(format, new TranscriptTextOptions { Layout = layout }));
    }

    [Fact]
    public void AnUnknownLayoutIsRefusedInWords()
    {
        Assert.Null(TranscriptText.Validate(null));
        Assert.Null(TranscriptText.Validate(new TranscriptTextOptions { Layout = "lines" }));
        Assert.Equal("Transcript layout 'pages' is not available. Choose auto, turns, lines.", TranscriptText.Validate(new TranscriptTextOptions { Layout = "pages" }));
        Assert.Equal(
            "Transcript layout 'pages' is not available. Choose auto, turns, lines.",
            ExportRules.Validate(ExportSelection.Default with { Transcript = new ExportTranscriptChoice { On = true, Formats = ["text"], Options = new TranscriptTextOptions { Layout = "pages" } } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TranscriptText.Format("srt", Sample(), Summary()));
    }
}
