using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><c>transcript.joinSegments</c> (2.6.0) through the bridge: a line joined with the next line of its track, the Undo of a split.</summary>
public sealed class JoinSegmentsMethodTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static VoicesDocument Voices() => new(
        VoicesDocument.CurrentSchemaVersion,
        "nemo-titanet-small",
        "sig",
        [],
        [new VoiceCluster("mic", 0, [1f, 0f], 10, ["s0001", "s0003"]), new VoiceCluster("mic", 1, [0f, 1f], 10, ["s0002"])]);

    private async Task<string> RecordingAsync(TranscriptDocument? transcript = null)
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => transcript ?? Meeting(), CancellationToken.None);
        await _host.Transcripts.SaveVoicesAsync(id, Voices(), CancellationToken.None);
        return id;
    }

    private Task<JsonElement> ResultAsync(string method, object parameters) => _host.ResultAsync(method, JsonSerializer.Serialize(parameters));

    private async Task<(string Code, string? Detail, string Message)> ErrorAsync(object parameters)
    {
        var response = await _host.CallAsync("transcript.joinSegments", JsonSerializer.Serialize(parameters));
        var error = response.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.TryGetProperty("detail", out var detail) ? detail.GetString() : null, error.GetProperty("message").GetString()!);
    }

    private async Task<TranscriptDocument> LoadAsync(string id) => (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task JoiningTheTwoPartsOfASplitLineGivesTheLineBack()
    {
        var id = await RecordingAsync();
        var original = (await LoadAsync(id)).Segments[0];
        await ResultAsync("transcript.splitSegment", new { recordingId = id, segmentId = "s0001", at = 10 });
        var before = (await LoadAsync(id)).Version;

        var result = await ResultAsync("transcript.joinSegments", new { recordingId = id, segmentId = "s0001" });

        Assert.Equal("s0001-2", result.GetProperty("removedId").GetString());
        var joined = result.GetProperty("segment");
        Assert.Equal((original.Text, "spk1"), (joined.GetProperty("text").GetString(), joined.GetProperty("speaker").GetString()));
        Assert.Equal(["spk1", "spk2"], result.GetProperty("speakers").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        var after = await LoadAsync(id);
        Assert.Equal(before + 1, after.Version);
        Assert.Equal(["s0001", "s0002", "s0003"], after.Segments.Select(s => s.Id));
        Assert.Equal((original.Text, original.Start, original.End, original.Confidence), (after.Segments[0].Text, after.Segments[0].Start, after.Segments[0].End, after.Segments[0].Confidence));
        Assert.Equal(original.Words, after.Segments[0].Words);
        Assert.Null(after.Segments[0].Edited);
        Assert.Equal(["s0001", "s0003"], (await _host.Transcripts.LoadVoicesAsync(id, CancellationToken.None))!.Clusters[0].SegmentIds);
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Lines joined" && h.Detail == "at 0:00");
    }

    [Fact]
    public async Task AHighlightOnTheLineThatGoesMovesToTheJoinedLine()
    {
        var id = await RecordingAsync();
        await ResultAsync("transcript.splitSegment", new { recordingId = id, segmentId = "s0001", at = 10 });
        await _host.Store.UpdateAnnotationsAsync(
            id,
            d => d with { Highlights = [new Highlight("h1", 500, "", "user", "s0001"), new Highlight("h2", 1500, "Budget", "user", "s0001-2"), new Highlight("h3", 6000, "", "user", "s0002")] },
            CancellationToken.None);

        var result = await ResultAsync("transcript.joinSegments", new { recordingId = id, segmentId = "s0001" });

        Assert.Equal(["s0001", "s0001", "s0002"], result.GetProperty("highlights").EnumerateArray().Select(h => h.GetProperty("segmentId").GetString()));
        var stored = await _host.Store.LoadAnnotationsAsync(id, CancellationToken.None);
        Assert.Equal(["s0001", "s0001", "s0002"], stored.Highlights.Select(h => h.SegmentId));
    }

    [Fact]
    public async Task TwoLinesTheEngineWroteJoinUneditedAndTheResultAnswersTheHighlightsAsTheyAre()
    {
        var id = await RecordingAsync();

        var result = await ResultAsync("transcript.joinSegments", new { recordingId = id, segmentId = "s0001" });

        Assert.Equal("s0002", result.GetProperty("removedId").GetString());
        Assert.Equal("Welcome everyone to the planning meeting. Thanks. Let's review the budget first.", result.GetProperty("segment").GetProperty("text").GetString());
        Assert.Empty(result.GetProperty("highlights").EnumerateArray());
        var after = await LoadAsync(id);
        Assert.Equal(["s0001", "s0003"], after.Segments.Select(s => s.Id));
        Assert.Equal((0.0, 9.0), (after.Segments[0].Start, after.Segments[0].End));
        // The joined line keeps the first line's speaker, so Speaker 1 holds 0–9 and 9.5–15; Speaker 2 is left with nothing.
        Assert.Equal([14_500L, 0L], after.Speakers.Select(s => s.TalkTimeMs));
    }

    [Fact]
    public async Task TheLastLineAndALineBeforeAnotherTrackAreRefusedWithNothingWritten()
    {
        var id = await RecordingAsync();
        var before = (await LoadAsync(id)).Version;

        var (code, detail, message) = await ErrorAsync(new { recordingId = id, segmentId = "s0003" });
        Assert.Equal(("transcript.cannotJoin", "s0003", "There is no line after this one. Nothing was changed."), (code, detail, message));

        var mixed = await RecordingAsync(Document(
            Segment("s0001", 0, 4, "Welcome everyone.", "spk1"),
            Segment("s0002", 4.5, 9, "Thanks.", "spk2", track: "system")));
        var (other, _, why) = await ErrorAsync(new { recordingId = mixed, segmentId = "s0001" });
        Assert.Equal(("transcript.cannotJoin", "The next line is on another track, so the two cannot be joined. Nothing was changed."), (other, why));

        Assert.Equal(before, (await LoadAsync(id)).Version);
        Assert.Equal(("transcript.segmentNotFound", "s0009"), (await ErrorAsync(new { recordingId = id, segmentId = "s0009" })) is var (c, d, _) ? (c, d) : default);
    }
}
