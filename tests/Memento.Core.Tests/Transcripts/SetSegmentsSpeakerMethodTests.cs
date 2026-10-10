using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><c>transcript.setSegmentsSpeaker</c> (2.0 selection mode) through the bridge.</summary>
public sealed class SetSegmentsSpeakerMethodTests : IDisposable
{
    private static readonly string[] FirstAndThird = ["s0001", "s0003"];
    private static readonly string[] SecondAndThird = ["s0002", "s0003"];
    private static readonly string[] OneUnknown = ["s0001", "s9999"];
    private static readonly string[] First = ["s0001"];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<string> RecordingAsync()
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => Meeting(), CancellationToken.None);
        return id;
    }

    private Task<JsonElement> CallAsync(object parameters) => _host.CallAsync("transcript.setSegmentsSpeaker", JsonSerializer.Serialize(parameters));

    [Fact]
    public async Task SeveralLinesMoveToOneSpeakerInOneWrite()
    {
        var id = await RecordingAsync();
        var before = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Version;

        var result = (await CallAsync(new { recordingId = id, segmentIds = FirstAndThird, speakerId = "spk2" })).GetProperty("result");

        Assert.Equal(["s0001", "s0003"], result.GetProperty("segments").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.All(result.GetProperty("segments").EnumerateArray(), s => Assert.Equal("spk2", s.GetProperty("speaker").GetString()));
        var after = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;
        Assert.Equal(before + 1, after.Version);
        Assert.All(after.Segments, s => Assert.Equal("spk2", s.Speaker));
        Assert.Equal([0L, 14_000L], after.Speakers.Select(s => s.TalkTimeMs));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Speaker changed" && h.Detail == "2 lines");
    }

    [Fact]
    public async Task ANewSpeakerTakesTheLines()
    {
        var id = await RecordingAsync();

        var result = (await CallAsync(new { recordingId = id, segmentIds = SecondAndThird, speakerId = (string?)null, newSpeakerName = "Ana" })).GetProperty("result");

        var created = result.GetProperty("speakers").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Ana");
        Assert.Equal("spk3", created.GetProperty("id").GetString());
        Assert.True(created.GetProperty("renamed").GetBoolean());
        Assert.All(result.GetProperty("segments").EnumerateArray(), s => Assert.Equal("spk3", s.GetProperty("speaker").GetString()));
    }

    [Fact]
    public async Task AnUnknownLineOrSpeakerChangesNothing()
    {
        var id = await RecordingAsync();
        var before = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Version;

        var line = await CallAsync(new { recordingId = id, segmentIds = OneUnknown, speakerId = "spk2" });
        var speaker = await CallAsync(new { recordingId = id, segmentIds = First, speakerId = "spk9" });
        var none = await CallAsync(new { recordingId = id, segmentIds = Array.Empty<string>(), speakerId = "spk2" });

        Assert.Equal("transcript.segmentNotFound", line.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("s9999", line.GetProperty("error").GetProperty("detail").GetString());
        Assert.Equal("transcript.speakerNotFound", speaker.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("bridge.invalidParams", none.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Version);
    }
}
