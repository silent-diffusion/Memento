using System.Text.Json;
using System.Text.RegularExpressions;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Transcripts;

/// <summary>A cluster's voice fit (2.6.0) in <c>voices.json</c>: written when known, left out otherwise, and absent from older files.</summary>
public sealed class VoicesDocumentFitTests
{
    [Fact]
    public void AFitIsWrittenWhenKnownAndLeftOutOtherwise()
    {
        var document = new VoicesDocument(
            VoicesDocument.CurrentSchemaVersion,
            "nemo-titanet-small",
            "sig",
            [],
            [new VoiceCluster("mic", 0, [1f, 0f], 10, ["s1"]) { Fit = 0.3125 }, new VoiceCluster("mic", 1, [0f, 1f], 5, ["s2"])]);

        var json = JsonSerializer.Serialize(document, TranscriptJsonContext.Default.VoicesDocument);

        // The context may indent; only the value and that it appears once matter.
        Assert.Matches("\"fit\":\\s*0\\.3125", json);
        Assert.Single(Regex.Matches(json, "\"fit\""));
        var back = JsonSerializer.Deserialize(json, TranscriptJsonContext.Default.VoicesDocument)!;
        Assert.Equal([0.3125, null], back.Clusters.Select(c => c.Fit));
        Assert.Equal([["s1"], ["s2"]], back.Clusters.Select(c => c.SegmentIds.ToArray()));
    }

    [Fact]
    public void AFileFromBeforeReadsWithoutFitsAndKeepsItsSchema()
    {
        const string json = """{"schemaVersion":1,"embeddingModelId":"m","signature":"s","tracks":[],"clusters":[{"track":"mic","speaker":0,"embedding":[1,0],"seconds":10,"segmentIds":["s1"]}]}""";

        var back = JsonSerializer.Deserialize(json, TranscriptJsonContext.Default.VoicesDocument)!;

        Assert.Equal(VoicesDocument.CurrentSchemaVersion, back.SchemaVersion);
        var cluster = Assert.Single(back.Clusters);
        Assert.Null(cluster.Fit);
        Assert.Equal(["s1"], cluster.SegmentIds);
    }
}
