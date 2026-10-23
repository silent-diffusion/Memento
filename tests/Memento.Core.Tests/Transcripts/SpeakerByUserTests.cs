using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary>
/// <c>speakerByUser</c> (2.6.0, BRIDGE.md › Corrections guide the next pass): set where the user places a line, kept by
/// the methods that act on speakers, left out of the JSON while false.
/// </summary>
public sealed class SpeakerByUserTests : IDisposable
{
    private static readonly string[] FirstAndThird = ["s0001", "s0003"];
    private static readonly string[] Third = ["s0003"];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<string> RecordingAsync()
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => Meeting(), CancellationToken.None);
        return id;
    }

    private Task<JsonElement> CallAsync(string method, object parameters) => _host.CallAsync(method, JsonSerializer.Serialize(parameters));

    private async Task<Dictionary<string, bool>> FlagsAsync(string id) =>
        (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Segments.ToDictionary(s => s.Id, s => s.SpeakerByUser, StringComparer.Ordinal);

    [Fact]
    public async Task PlacingALineMarksItAndSettingNoSpeakerClearsIt()
    {
        var id = await RecordingAsync();
        Assert.All((await FlagsAsync(id)).Values, Assert.False);

        var moved = (await CallAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", speakerId = "spk2" })).GetProperty("result");
        Assert.True(moved.GetProperty("segment").GetProperty("speakerByUser").GetBoolean());
        var created = (await CallAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0002", speakerId = (string?)null, newSpeakerName = "Ana" })).GetProperty("result");
        Assert.True(created.GetProperty("segment").GetProperty("speakerByUser").GetBoolean());
        Assert.Equal(new Dictionary<string, bool> { ["s0001"] = true, ["s0002"] = true, ["s0003"] = false }, await FlagsAsync(id));

        var cleared = (await CallAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", speakerId = (string?)null })).GetProperty("result");
        Assert.False(cleared.GetProperty("segment").TryGetProperty("speakerByUser", out _));
        Assert.False((await FlagsAsync(id))["s0001"]);
    }

    [Fact]
    public async Task SeveralLinesAreMarkedInOneWrite()
    {
        var id = await RecordingAsync();

        var result = (await CallAsync("transcript.setSegmentsSpeaker", new { recordingId = id, segmentIds = FirstAndThird, speakerId = "spk2" })).GetProperty("result");

        Assert.All(result.GetProperty("segments").EnumerateArray(), s => Assert.True(s.GetProperty("speakerByUser").GetBoolean()));
        Assert.Equal(new Dictionary<string, bool> { ["s0001"] = true, ["s0002"] = false, ["s0003"] = true }, await FlagsAsync(id));
    }

    [Fact]
    public async Task MergeRestoreAndReduceKeepEachLinesMark()
    {
        var id = await RecordingAsync();
        await CallAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", speakerId = "spk2" });

        // The merge moves s0003 (spk1) into spk2 without marking it; s0001 stays marked.
        await CallAsync("transcript.mergeSpeakers", new { recordingId = id, fromSpeakerId = "spk1", intoSpeakerId = "spk2" });
        Assert.Equal(new Dictionary<string, bool> { ["s0001"] = true, ["s0002"] = false, ["s0003"] = false }, await FlagsAsync(id));

        // Undo of the merge puts spk1 back with s0003: still not marked.
        await CallAsync("transcript.restoreSpeaker", new { recordingId = id, speaker = new { id = "spk1", name = "Speaker 1", color = 1, renamed = false }, segmentIds = Third });
        Assert.Equal(new Dictionary<string, bool> { ["s0001"] = true, ["s0002"] = false, ["s0003"] = false }, await FlagsAsync(id));

        await CallAsync("transcript.reduceSpeakers", new { recordingId = id, count = 1 });
        Assert.Equal(new Dictionary<string, bool> { ["s0001"] = true, ["s0002"] = false, ["s0003"] = false }, await FlagsAsync(id));
        Assert.Single((await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Speakers);
    }

    [Fact]
    public void TheMarkIsLeftOutWhileFalseAndRoundTripsWhenTrue()
    {
        var line = new TranscriptSegment("s0001", 12.4, 15.92, "system", "spk1", 1, "Hello there.", 0.91, [], null);
        var placed = line with { SpeakerByUser = true };

        Assert.Equal(
            """{"segment":{"id":"s0001","start":12.4,"end":15.92,"track":"system","speaker":"spk1","speakerConfidence":1,"text":"Hello there.","confidence":0.91,"words":[],"edited":null},"speakers":[]}""",
            JsonSerializer.Serialize(new SegmentSpeakersResult(line, []), BridgeJsonContext.Default.SegmentSpeakersResult));
        var json = JsonSerializer.Serialize(new SegmentSpeakersResult(placed, []), BridgeJsonContext.Default.SegmentSpeakersResult);
        Assert.EndsWith(""","edited":null,"speakerByUser":true},"speakers":[]}""", json, StringComparison.Ordinal);
        Assert.True(JsonSerializer.Deserialize(json, BridgeJsonContext.Default.SegmentSpeakersResult)!.Segment.SpeakerByUser);
    }

    [Fact]
    public void ATranscriptFromBeforeReadsAsNotPlaced()
    {
        // A line as 2.5 wrote it, without the field.
        const string Json = """{"segment":{"id":"s0001","start":12.4,"end":15.92,"track":"system","speaker":"spk1","speakerConfidence":0.82,"text":"Hello there.","confidence":0.91,"words":[],"edited":null},"speakers":[]}""";

        Assert.False(JsonSerializer.Deserialize(Json, BridgeJsonContext.Default.SegmentSpeakersResult)!.Segment.SpeakerByUser);
    }
}
