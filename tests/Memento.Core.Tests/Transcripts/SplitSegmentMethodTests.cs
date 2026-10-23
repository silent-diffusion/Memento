using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><c>transcript.splitSegment</c> (2.6.0) through the bridge: one line cut in two at a word, in one write.</summary>
public sealed class SplitSegmentMethodTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static VoicesDocument Voices() => new(
        VoicesDocument.CurrentSchemaVersion,
        "nemo-titanet-small",
        "sig",
        [],
        [new VoiceCluster("mic", 0, [1f, 0f], 10, ["s0001", "s0003"]), new VoiceCluster("mic", 1, [0f, 1f], 10, ["s0002"])]);

    private async Task<string> RecordingAsync()
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => Meeting(), CancellationToken.None);
        await _host.Transcripts.SaveVoicesAsync(id, Voices(), CancellationToken.None);
        return id;
    }

    private Task<JsonElement> ResultAsync(object parameters) => _host.ResultAsync("transcript.splitSegment", JsonSerializer.Serialize(parameters));

    private async Task<(string Code, string? Detail, string Message)> ErrorAsync(object parameters)
    {
        var response = await _host.CallAsync("transcript.splitSegment", JsonSerializer.Serialize(parameters));
        var error = response.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.TryGetProperty("detail", out var detail) ? detail.GetString() : null, error.GetProperty("message").GetString()!);
    }

    private async Task<TranscriptDocument> LoadAsync(string id) => (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task TheLineIsCutAtTheNearestWordInOneWrite()
    {
        var id = await RecordingAsync();
        var before = (await LoadAsync(id)).Version;

        var result = await ResultAsync(new { recordingId = id, segmentId = "s0001", at = 10 });

        var parts = result.GetProperty("segments").EnumerateArray().ToList();
        Assert.Equal(["s0001", "s0001-2"], parts.Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(["Welcome", "everyone to the planning meeting."], parts.Select(p => p.GetProperty("text").GetString()));
        Assert.Equal(["spk1", "spk1"], parts.Select(p => p.GetProperty("speaker").GetString()));
        Assert.Equal(["spk1", "spk2"], result.GetProperty("speakers").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        var after = await LoadAsync(id);
        Assert.Equal(before + 1, after.Version);
        Assert.Equal(["s0001", "s0001-2", "s0002", "s0003"], after.Segments.Select(s => s.Id));
        var first = after.Segments[0];
        var second = after.Segments[1];
        Assert.Equal((0.0, first.Words[^1].E, second.Words[0].S, 4.0), (first.Start, first.End, second.Start, second.End));
        Assert.Equal(["Welcome"], first.Words.Select(w => w.W));
        // Talk time follows the parts (the pause between the words is no one's now).
        Assert.Equal((long)Math.Round(((first.End - first.Start) + (second.End - second.Start) + 5.5) * 1000), after.Speakers[0].TalkTimeMs);
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Line split" && h.Detail == "at 0:00 · \"Welcome\" | \"everyone to the…\"");
    }

    [Fact]
    public async Task TheVoiceThatHeldTheLineHoldsBothParts()
    {
        var id = await RecordingAsync();

        await ResultAsync(new { recordingId = id, segmentId = "s0001", at = 10 });

        var voices = (await _host.Transcripts.LoadVoicesAsync(id, CancellationToken.None))!;
        Assert.Equal(["s0001", "s0001-2", "s0003"], voices.Clusters[0].SegmentIds);
        Assert.Equal(["s0002"], voices.Clusters[1].SegmentIds);
    }

    [Fact]
    public async Task ACutOutsideTheWordsOrAnUnknownLineChangesNothing()
    {
        var id = await RecordingAsync();
        var before = (await LoadAsync(id)).Version;

        var (code, _, message) = await ErrorAsync(new { recordingId = id, segmentId = "s0001", at = 0 });
        Assert.Equal(("bridge.invalidParams", "Choose a place inside the line's words."), (code, message));
        var (notFound, detail, _) = await ErrorAsync(new { recordingId = id, segmentId = "s0009", at = 3 });
        Assert.Equal(("transcript.segmentNotFound", "s0009"), (notFound, detail));
        Assert.Equal("project.notFound", (await ErrorAsync(new { recordingId = "20260101-000000-nope", segmentId = "s0001", at = 3 })).Code);

        Assert.Equal(before, (await LoadAsync(id)).Version);
        Assert.Equal(["s0001", "s0003"], (await _host.Transcripts.LoadVoicesAsync(id, CancellationToken.None))!.Clusters[0].SegmentIds);
    }
}
