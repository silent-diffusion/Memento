using System.Text.Json.Nodes;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class DocumentJsonTests
{
    public static TheoryData<string> Fixtures => new() { "meeting-minutes", "all-shapes" };

    private static Document Fixture(string name) => name == "meeting-minutes" ? SampleDocuments.MeetingMinutes() : SampleDocuments.AllShapes();

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureSerializesToItsSnapshot(string name) =>
        Snapshot.Match($"{name}.document.json", DocumentJson.Serialize(Fixture(name)) + "\n");

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SnapshotRoundTripsUnchanged(string name)
    {
        var json = DocumentJson.Serialize(Fixture(name));
        var read = DocumentJson.Deserialize(json);
        Assert.Equal(json, DocumentJson.Serialize(read));
        Assert.Equal(Fixture(name).Modules().Count(), read.Modules().Count());
    }

    [Fact]
    public void BlocksAndRunsReadBackAsTheirTypes()
    {
        var read = DocumentJson.Deserialize(DocumentJson.Serialize(SampleDocuments.AllShapes()));
        var blocks = read.Modules().SelectMany(m => m.Blocks).ToList();
        Assert.Contains(blocks, b => b is HeadingBlock { Level: 2 });
        Assert.Contains(blocks, b => b is ListBlock { Style: ListStyle.Numbered });
        Assert.Contains(blocks, b => b is TableBlock { Columns.Count: 3 });
        Assert.Contains(blocks, b => b is ChipsBlock);
        Assert.Contains(blocks, b => b is LabelValueBlock);
        Assert.Contains(blocks, b => b is QuoteBlock { Attribution: "Alex Moreau", T: 512 });
        Assert.Contains(blocks, b => b is TimelineBlock);
        Assert.Contains(blocks, b => b is TranscriptBlock { Chapters.Count: 2, Segments.Count: 3 });
        var timestamp = blocks.OfType<ParagraphBlock>().SelectMany(p => p.Runs).First(r => r.Kind == RunKind.Timestamp);
        Assert.Equal(65.5, timestamp.T);
        Assert.Equal("1:05", timestamp.Text);
        Assert.Equal(TextSize.Larger, read.Rows[0].Modules[0].TextSize);
    }

    [Fact]
    public void JsonUsesCamelCaseNamesAndDiscriminators()
    {
        var node = JsonNode.Parse(DocumentJson.Serialize(SampleDocuments.AllShapes()))!;
        Assert.Equal(1, (int)node["schemaVersion"]!);
        var module = node["rows"]![0]!["modules"]![0]!;
        Assert.Equal("customText", (string)module["type"]!);
        Assert.Equal("larger", (string)module["textSize"]!);
        Assert.Equal("user", (string)module["provenance"]!["kind"]!);
        Assert.Equal("heading", (string)module["blocks"]![0]!["type"]!);
        var runs = module["blocks"]![1]!["runs"]!.AsArray();
        Assert.Equal("emphasis", (string)runs[1]!["kind"]!);
        Assert.Equal("bold", (string)runs[1]!["style"]!);
        Assert.Equal("timestamp", (string)runs[9]!["kind"]!);
        Assert.Equal(65.5, (double)runs[9]!["t"]!);
        Assert.Equal("smaller", (string)node["rows"]![2]!["modules"]![0]!["textSize"]!);
    }

    [Fact]
    public void UnknownFieldsAreKeptAtEveryLevel()
    {
        var node = JsonNode.Parse(DocumentJson.Serialize(SampleDocuments.AllShapes()))!.AsObject();
        node["futureTopLevel"] = new JsonObject { ["x"] = 1 };
        node["meta"]!["futureMeta"] = "kept";
        var module = node["rows"]![0]!["modules"]![0]!.AsObject();
        module["futureModule"] = true;
        module["provenance"]!["futureProvenance"] = 3;
        node["rows"]![0]!["futureRow"] = "row";
        var paragraph = module["blocks"]![1]!.AsObject();
        paragraph["futureBlock"] = new JsonArray(1, 2);
        paragraph["runs"]![0]!["futureRun"] = "run";
        module["blocks"]!.AsArray().Add(new JsonObject { ["type"] = "diagram", ["nodes"] = new JsonArray("a", "b"), ["edges"] = 2 });

        var json = node.ToJsonString();
        var read = DocumentJson.Deserialize(json);
        var again = JsonNode.Parse(DocumentJson.Serialize(read))!;

        Assert.Equal(1, (int)again["futureTopLevel"]!["x"]!);
        Assert.Equal("kept", (string)again["meta"]!["futureMeta"]!);
        Assert.True((bool)again["rows"]![0]!["modules"]![0]!["futureModule"]!);
        Assert.Equal(3, (int)again["rows"]![0]!["modules"]![0]!["provenance"]!["futureProvenance"]!);
        Assert.Equal("row", (string)again["rows"]![0]!["futureRow"]!);
        Assert.Equal(2, again["rows"]![0]!["modules"]![0]!["blocks"]![1]!["futureBlock"]!.AsArray().Count);
        Assert.Equal("run", (string)again["rows"]![0]!["modules"]![0]!["blocks"]![1]!["runs"]![0]!["futureRun"]!);
        var unknown = again["rows"]![0]!["modules"]![0]!["blocks"]!.AsArray().Last()!;
        Assert.Equal("diagram", (string)unknown["type"]!);
        Assert.Equal(2, (int)unknown["edges"]!);
        Assert.IsType<UnknownBlock>(read.Rows[0].Modules[0].Blocks[^1]);
        Assert.Equal("diagram", read.Rows[0].Modules[0].Blocks[^1].Type);
    }

    [Fact]
    public void NewerSchemaIsRefusedWithASpecificMessage()
    {
        var node = JsonNode.Parse(DocumentJson.Serialize(SampleDocuments.AllShapes()))!;
        node["schemaVersion"] = 2;
        var error = Assert.Throws<DocumentFormatException>(() => DocumentJson.Deserialize(node.ToJsonString(), "doc-all-shapes.json"));
        Assert.Equal(DocumentFormatErrorCodes.NewerVersion, error.Code);
        Assert.Contains("doc-all-shapes.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("newer version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidJsonIsReportedWithTheFileName()
    {
        var error = Assert.Throws<DocumentFormatException>(() => DocumentJson.Deserialize("{ \"rows\": [", "broken.json"));
        Assert.Equal(DocumentFormatErrorCodes.Invalid, error.Code);
        Assert.StartsWith("broken.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowsMustHoldOneToThreeModulesWithUniqueIds()
    {
        var doc = SampleDocuments.AllShapes();
        var four = doc with { Rows = [.. doc.Rows, DocumentRow.Of(doc.Rows[2].Modules[0], doc.Rows[2].Modules[1], doc.Rows[2].Modules[2], doc.Rows[0].Modules[0])] };
        var problems = DocumentJson.Validate(four);
        Assert.Contains(problems, p => p.Contains("holds 4 modules", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("used more than once", StringComparison.Ordinal));
        Assert.Empty(DocumentJson.Validate(doc));

        var error = Assert.Throws<DocumentFormatException>(() => DocumentJson.Deserialize(DocumentJson.Serialize(four)));
        Assert.Equal(DocumentFormatErrorCodes.Structure, error.Code);
    }

    [Fact]
    public async Task FileWriteIsAtomicAndReadsBack()
    {
        using var folder = new TempFolder();
        var path = folder.File("doc.json");
        await DocumentJson.WriteFileAsync(path, SampleDocuments.MeetingMinutes(), CancellationToken.None);
        Assert.False(File.Exists(path + ".tmp"));
        var read = await DocumentJson.ReadFileAsync(path, CancellationToken.None);
        Assert.Equal(DocumentJson.Serialize(SampleDocuments.MeetingMinutes()), DocumentJson.Serialize(read));
    }

    [Theory]
    [InlineData(0, "0:00", "0:00:00")]
    [InlineData(65.9, "1:05", "0:01:05")]
    [InlineData(1122, "18:42", "0:18:42")]
    [InlineData(3725, "1:02:05", "1:02:05")]
    public void TimecodesFollowTheDesignFormat(double seconds, string display, string full)
    {
        Assert.Equal(display, Timecode.Format(seconds));
        Assert.Equal(full, Timecode.FormatLong(seconds));
        Assert.Equal($"{display} — see the recording at {full}", Timecode.FootnoteText(seconds, null));
    }

    [Fact]
    public void MetaLineMatchesTheViewerDesign()
    {
        Assert.Equal("Meeting minutes · Monday 5 October 2026, 4:00 PM · 1 h 10 min · Zoom", MetaLine.Format(SampleDocuments.MeetingMinutes().Meta));
        Assert.Equal("42 min", MetaLine.Duration(42 * 60 * 1000));
        Assert.Equal("2 h", MetaLine.Duration(2 * 3600 * 1000));
        Assert.Equal("1 participant", MetaLine.Format(new DocumentMeta { ParticipantCount = 1 }));
        Assert.Equal(("Design review: library screen", "5 October 2026"), MetaLine.RunningHeader(SampleDocuments.MeetingMinutes()));
    }
}
