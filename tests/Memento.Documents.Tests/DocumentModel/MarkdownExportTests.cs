using Memento.Documents.Export;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class MarkdownExportTests
{
    private static readonly MarkdownExporter Exporter = new();

    [Fact]
    public void MeetingMinutesSnapshot() => Snapshot.Match("meeting-minutes.md", Exporter.Export(SampleDocuments.MeetingMinutes()));

    [Fact]
    public void AllShapesSnapshot() => Snapshot.Match("all-shapes.md", Exporter.Export(SampleDocuments.AllShapes()));

    [Fact]
    public void RowsAreStackedAndTimestampsAreInlineMarkers()
    {
        var md = Exporter.Export(SampleDocuments.MeetingMinutes());
        Assert.StartsWith("# Design review: library screen\n\nMeeting minutes · Monday 5 October 2026, 4:00 PM", md, StringComparison.Ordinal);
        var purpose = md.IndexOf("## Meeting purpose", StringComparison.Ordinal);
        var participants = md.IndexOf("## Participants", StringComparison.Ordinal);
        Assert.True(purpose > 0 && participants > purpose);
        Assert.Contains("is active. [18:42] The dark theme", md, StringComparison.Ordinal);
        Assert.Contains("| Action | Owner | Due |\n| --- | --- | --- |\n| Update the library mockup [19:20] | Priya | Wed |", md, StringComparison.Ordinal);
        Assert.Contains("**When:** Thursday 9 October, 4:00 PM  \n**Agenda:** Recording screen walkthrough\n", md, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", md, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFullTranscriptIsEmittedInFull()
    {
        var md = Exporter.Export(SampleDocuments.MeetingMinutes());
        Assert.Equal(72, md.Split("\n**", StringSplitOptions.None).Count(s => s.Contains("** [", StringComparison.Ordinal)));
        Assert.Contains("### Dark theme scope\n\n", md, StringComparison.Ordinal);
        Assert.Contains("**Sam Okafor** [0:15] Let's start with the mockups", md, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownSyntaxInTextIsEscaped()
    {
        var md = Exporter.Export(SampleDocuments.AllShapes());
        Assert.Contains("\\<tag\\> & \"quotes\" \\* \\_ \\[x\\] \\# 50% | pipe.", md, StringComparison.Ordinal);
        Assert.Contains("**bold**, *italic*, ***both***", md, StringComparison.Ordinal);
        Assert.Contains("- First point\n  - Nested point\n  - Nested with **emphasis**\n    - Third level\n- Second point [2:10]\n", md, StringComparison.Ordinal);
        Assert.Contains("1. Opening\n   1. Welcome\n   2. Minutes of the last meeting\n2. Budget\n3. Close\n", md, StringComparison.Ordinal);
        Assert.Contains("> We should ship the smaller thing first.\n>\n> — Alex Moreau [8:32]\n", md, StringComparison.Ordinal);
        Assert.Contains("- [1:02:05] **Close**\n", md, StringComparison.Ordinal);
    }
}
