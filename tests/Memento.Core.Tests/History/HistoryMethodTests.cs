using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.History;

/// <summary><c>history.links</c> and <c>transcript.getVersion</c> through the bridge, on a recording with a synthetic transcript.</summary>
public sealed class HistoryMethodTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<JsonElement> ResultAsync(string method, object parameters) => _host.ResultAsync(method, JsonSerializer.Serialize(parameters));

    private async Task<string> RecordingWithTranscriptAsync()
    {
        var id = await _host.RecordAsync("Planning meeting", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting(), CancellationToken.None);
        await _host.Store.AppendHistoryAsync(id, new HistoryEntry(DateTimeOffset.Now, "transcript", "completed", "Transcribed · 3 lines", null), CancellationToken.None);
        await Task.Delay(20);
        return id;
    }

    private async Task<List<(string Summary, string? VersionId, string After)>> LinksAsync(string id)
    {
        var history = (await ResultAsync("project.get", new { recordingId = id })).GetProperty("history").EnumerateArray().ToList();
        return (await ResultAsync("history.links", new { recordingId = id })).GetProperty("links").EnumerateArray()
            .Select(l => (
                history[l.GetProperty("index").GetInt32()].GetProperty("summary").GetString()!,
                l.GetProperty("versionId").GetString(),
                l.GetProperty("after").GetString()!))
            .ToList();
    }

    [Fact]
    public async Task EachTranscriptLineOpensTheCopyItMadeAndARestoreKeepsThemAll()
    {
        var id = await RecordingWithTranscriptAsync();
        await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0001", text = "Welcome, all." });
        await Task.Delay(20);

        var links = await LinksAsync(id);
        var original = Assert.Single((await ResultAsync("transcript.versions", new { recordingId = id })).GetProperty("versions").EnumerateArray()).GetProperty("id").GetString()!;
        Assert.Equal([("Transcribed · 3 lines", original, "after it was transcribed"), ("Transcript edited", "current", "after a line was edited")], links);

        // The kept copy reads whole, as it was.
        var version = await ResultAsync("transcript.getVersion", new { recordingId = id, versionId = original });
        Assert.Equal("Welcome everyone to the planning meeting.", version.GetProperty("transcript").GetProperty("segments")[0].GetProperty("text").GetString());

        // Restoring it keeps the edited transcript as a version, which the edit's line now opens.
        await ResultAsync("transcript.restoreVersion", new { recordingId = id, versionId = original });
        await Task.Delay(20);
        var after = await LinksAsync(id);
        var edited = (await ResultAsync("transcript.versions", new { recordingId = id })).GetProperty("versions")[0].GetProperty("id").GetString();
        Assert.Equal(
            [("Transcribed · 3 lines", original, "after it was transcribed"), ("Transcript edited", edited, "after a line was edited"), ("Transcript version restored", "current", "after an earlier version was restored")],
            after);
        Assert.Equal("Welcome, all.", (await ResultAsync("transcript.getVersion", new { recordingId = id, versionId = edited })).GetProperty("transcript").GetProperty("segments")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task WithVersionHistoryOffOnlyTheCurrentTranscriptOpens()
    {
        var id = await RecordingWithTranscriptAsync();
        await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0001", text = "Welcome, all." });
        await _host.ResultAsync("settings.set", """{"history":{"keepVersions":false}}""");

        Assert.Equal([("Transcribed · 3 lines", null, "after it was transcribed"), ("Transcript edited", "current", "after a line was edited")], await LinksAsync(id));
    }

    [Fact]
    public async Task ARecordingWithoutATranscriptHasNoLinksAndUnknownVersionsAreRefused()
    {
        var id = await _host.RecordAsync("Plain", 1, Mic);

        Assert.Empty((await ResultAsync("history.links", new { recordingId = id })).GetProperty("links").EnumerateArray());
        var missing = await _host.CallAsync("transcript.getVersion", JsonSerializer.Serialize(new { recordingId = id, versionId = "20200101T000000000Z" }));
        Assert.Equal("transcript.versionNotFound", missing.GetProperty("error").GetProperty("code").GetString());
        var unknown = await _host.CallAsync("history.links", JsonSerializer.Serialize(new { recordingId = "20990101-000000-zzzzzz" }));
        Assert.Equal("project.notFound", unknown.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void TheLinksShapeIsPinned()
    {
        Assert.Equal(
            """{"links":[{"index":7,"kind":"transcript","documentId":null,"versionId":"20261006T090000000Z","after":"after speakers were identified"},{"index":9,"kind":"document","documentId":"d1","versionId":null,"after":"after it was edited"}]}""",
            JsonSerializer.Serialize(
                new HistoryLinksResult([new HistoryLink(7, "transcript", null, "20261006T090000000Z", "after speakers were identified"), new HistoryLink(9, "document", "d1", null, "after it was edited")]),
                Memento.Core.Bridge.BridgeJsonContext.Default.HistoryLinksResult));
    }
}
