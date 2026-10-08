using Memento.Core.Bridge.Contracts;
using Memento.Core.History;

namespace Memento.Core.Tests.History;

public sealed class HistoryLinkerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(-6));

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private static HistoryEntry Line(double minutes, string stage, string @event, string summary) => new(At(minutes), stage, @event, summary, null);

    private static HistorySnapshot Transcript(string id, double start, double? end) => new(HistoryKinds.Transcript, null, id, At(start), end is { } e ? At(e) : null);

    private static HistorySnapshot Document(string documentId, string id, double start, double? end) => new(HistoryKinds.Document, documentId, id, At(start), end is { } e ? At(e) : null);

    [Fact]
    public void EachCopyOpensFromTheLastLineThatMadeIt()
    {
        // Transcribed at 10:00, speakers at 10:04, an edit at 10:10 kept the 10:00–10:10 copy; the edit is current.
        HistoryEntry[] history =
        [
            Line(-1, "recorded", "completed", "Recording stopped"),
            Line(0, "transcript", "completed", "Transcribed · 30 lines"),
            Line(4, "speakers", "completed", "Found 2 speakers"),
            Line(4, "topics", "completed", "Found 5 topics"),
            Line(10, "edited", "info", "Transcript edited"),
            Line(11, "edited", "info", "Details edited"),
        ];

        var links = HistoryLinker.Link(history, [Transcript("20261008T161000000Z", 0, 10), Transcript(HistorySnapshot.CurrentId, 10, null)]);

        Assert.Equal([1, 2, 4], links.Select(l => l.Index));
        Assert.Null(links[0].VersionId); // the transcript before speakers were identified was not kept
        Assert.Equal("20261008T161000000Z", links[1].VersionId);
        Assert.Equal("after speakers were identified", links[1].After);
        Assert.Equal(HistorySnapshot.CurrentId, links[2].VersionId);
        Assert.Equal("after a line was edited", links[2].After);
        Assert.All(links, l => Assert.Equal(HistoryKinds.Transcript, l.Kind));
    }

    [Fact]
    public void ARunOfEditsOpensFromItsLastLineAndAPrunedCopyFromNone()
    {
        // The first version (10:00–10:05) was pruned; a run of edits from 10:05 to 10:07 is current.
        HistoryEntry[] history =
        [
            Line(0, "transcript", "completed", "Transcribed · 30 lines"),
            Line(5, "edited", "info", "Speaker changed"),
            Line(6, "edited", "info", "Speakers merged"),
            Line(7, "edited", "info", "Speaker renamed"),
        ];

        var links = HistoryLinker.Link(history, [Transcript(HistorySnapshot.CurrentId, 7, null)]);

        Assert.Equal([null, null, null, HistorySnapshot.CurrentId], links.Select(l => l.VersionId));
        Assert.Equal("after a speaker was renamed", links[3].After);
    }

    [Fact]
    public void ARestoreOpensTheRestoredContentAndTheReplacedCopyStaysOpenable()
    {
        HistoryEntry[] history =
        [
            Line(0, "transcript", "completed", "Transcribed · 30 lines"),
            Line(5, "edited", "info", "Transcript edited"),
            Line(9, "edited", "info", "Transcript version restored"),
        ];
        HistorySnapshot[] copies =
        [
            Transcript("20261008T160500000Z", 0, 5),
            Transcript("20261008T160900000Z", 5, 9),
            Transcript(HistorySnapshot.CurrentId, 9, null),
        ];

        var links = HistoryLinker.Link(history, copies);

        Assert.Equal(["20261008T160500000Z", "20261008T160900000Z", HistorySnapshot.CurrentId], links.Select(l => l.VersionId));
        Assert.Equal("after an earlier version was restored", links[2].After);
    }

    [Fact]
    public void DocumentLinesOpenTheDocumentWrittenJustBefore()
    {
        HistoryEntry[] history =
        [
            Line(0, "minutes", "completed", "Meeting minutes generated with Local model (Qwen3.5 4B · graphics card)"),
            Line(3, "edited", "info", "Document \"Notes\" created"),
            Line(8, "edited", "info", "Document \"Meeting minutes\" edited"),
            Line(9, "edited", "info", "Document \"Meeting minutes\" exported"),
            Line(12, "minutes", "completed", "Meeting minutes regenerated with Claude (claude-opus-5-5)"),
            Line(13, "minutes", "failed", "Meeting minutes could not be generated"),
        ];
        HistorySnapshot[] copies =
        [
            Document("d1", "20261008T160800000Z", 0, 8),
            Document("d1", "20261008T161200000Z", 8, 12),
            Document("d1", HistorySnapshot.CurrentId, 12, null),
            Document("d2", HistorySnapshot.CurrentId, 3, null),
        ];

        var links = HistoryLinker.Link(history, copies);

        Assert.Equal([0, 1, 2, 4], links.Select(l => l.Index));
        Assert.Equal([("d1", "20261008T160800000Z"), ("d2", HistorySnapshot.CurrentId), ("d1", "20261008T161200000Z"), ("d1", HistorySnapshot.CurrentId)], links.Select(l => (l.DocumentId, l.VersionId)));
        Assert.Equal(["after it was generated", "after it was created", "after it was edited", "after it was generated again"], links.Select(l => l.After));
        Assert.All(links, l => Assert.Equal(HistoryKinds.Document, l.Kind));
    }

    [Fact]
    public void ADocumentLineLongAfterAnyWriteOpensNothing()
    {
        var links = HistoryLinker.Link([Line(30, "edited", "info", "Document \"Notes\" edited")], [Document("d2", HistorySnapshot.CurrentId, 3, null)]);

        var link = Assert.Single(links);
        Assert.Null(link.VersionId);
        Assert.Null(link.DocumentId);
    }

    [Theory]
    [InlineData("recorded", "completed", "Recording stopped")]
    [InlineData("stored", "completed", "Stored 2 tracks · 331 MB")]
    [InlineData("transcript", "failed", "Transcript failed")]
    [InlineData("transcript", "info", "Dropped 3 repeated lines")]
    [InlineData("transcript", "info", "Transcription queued again")]
    [InlineData("speakers", "started", "Identifying speakers")]
    [InlineData("topics", "completed", "Found 8 topics")]
    [InlineData("edited", "info", "Details edited")]
    [InlineData("edited", "info", "Renamed")]
    [InlineData("edited", "info", "Document \"Notes\" exported")]
    [InlineData("edited", "info", "Document renamed to \"Notes\"")]
    [InlineData("minutes", "failed", "Meeting minutes could not be generated")]
    public void LinesThatChangedNeitherAreNotListed(string stage, string @event, string summary)
    {
        Assert.Null(HistoryLinker.Describe(new HistoryEntry(T0, stage, @event, summary, null)));
    }

    [Fact]
    public void AnOlderTranscriptWithoutATimeTakesTheLinesBeforeItsReplacement()
    {
        HistoryEntry[] history = [Line(0, "transcript", "completed", "Transcribed · 3 lines"), Line(5, "edited", "info", "Transcript edited")];

        var links = HistoryLinker.Link(history, [new HistorySnapshot(HistoryKinds.Transcript, null, "20261008T160500000Z", null, At(5)), Transcript(HistorySnapshot.CurrentId, 5, null)]);

        Assert.Equal(["20261008T160500000Z", HistorySnapshot.CurrentId], links.Select(l => l.VersionId));
    }

    [Fact]
    public void AVersionIdIsTheMomentItWasReplaced()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 16, 10, 0, 123, TimeSpan.Zero), HistorySnapshot.StampOf("20261008T161000123Z"));
        Assert.Null(HistorySnapshot.StampOf(HistorySnapshot.CurrentId));
    }
}
