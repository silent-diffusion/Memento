using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><c>transcript.swapSpeakers</c> (2.6.0) through the bridge: two speakers trade their lines, everywhere or from a line on.</summary>
public sealed class SwapSpeakersMethodTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    // The fixture: s0001 (0–4 s) and s0003 (9.5–15 s) are Speaker 1's, s0002 (4.5–9 s) is Speaker 2's.
    private async Task<string> RecordingAsync()
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => Meeting(), CancellationToken.None);
        return id;
    }

    private Task<JsonElement> CallAsync(object parameters) => _host.CallAsync("transcript.swapSpeakers", JsonSerializer.Serialize(parameters));

    private async Task<TranscriptDocument> LoadAsync(string id) => (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task EverywhereTradesEveryLineBothWaysInOneWrite()
    {
        var id = await RecordingAsync();
        var before = (await LoadAsync(id)).Version;

        var result = (await CallAsync(new { recordingId = id, speakerId = "spk1", otherSpeakerId = "spk2" })).GetProperty("result");

        Assert.Equal(["s0001", "s0002", "s0003"], result.GetProperty("segments").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(["spk2", "spk1", "spk2"], result.GetProperty("segments").EnumerateArray().Select(s => s.GetProperty("speaker").GetString()));
        Assert.All(result.GetProperty("segments").EnumerateArray(), s => Assert.Equal(1.0, s.GetProperty("speakerConfidence").GetDouble()));
        var after = await LoadAsync(id);
        Assert.Equal(before + 1, after.Version);
        Assert.Equal(["spk2", "spk1", "spk2"], after.Segments.Select(s => s.Speaker));
        Assert.Equal(["spk1", "spk2"], after.Speakers.Select(s => s.Id));
        Assert.Equal(["Speaker 1", "Speaker 2"], after.Speakers.Select(s => s.Name));
        Assert.Equal([4_500L, 9_500L], after.Speakers.Select(s => s.TalkTimeMs));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Speakers swapped" && h.Detail == "Speaker 1 and Speaker 2 · 3 lines");
    }

    [Fact]
    public async Task FromALineOnLeavesTheEarlierLinesAlone()
    {
        var id = await RecordingAsync();

        var result = (await CallAsync(new { recordingId = id, speakerId = "spk2", otherSpeakerId = "spk1", fromSegmentId = "s0002" })).GetProperty("result");

        Assert.Equal(["s0002", "s0003"], result.GetProperty("segments").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        var after = await LoadAsync(id);
        Assert.Equal(["spk1", "spk1", "spk2"], after.Segments.Select(s => s.Speaker));
        Assert.Equal([8_500L, 5_500L], after.Speakers.Select(s => s.TalkTimeMs));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Speakers swapped" && h.Detail == "Speaker 2 and Speaker 1 from 0:04 · 2 lines");
    }

    [Fact]
    public async Task TheSameCallAgainPutsTheLinesBack()
    {
        var id = await RecordingAsync();
        var original = await LoadAsync(id);

        await CallAsync(new { recordingId = id, speakerId = "spk1", otherSpeakerId = "spk2", fromSegmentId = "s0002" });
        await CallAsync(new { recordingId = id, speakerId = "spk1", otherSpeakerId = "spk2", fromSegmentId = "s0002" });

        var after = await LoadAsync(id);
        Assert.Equal(original.Segments.Select(s => s.Speaker), after.Segments.Select(s => s.Speaker));
        Assert.Equal(original.Speakers.Select(s => s.TalkTimeMs), after.Speakers.Select(s => s.TalkTimeMs));
        Assert.Equal(original.Version + 2, after.Version);
    }

    [Fact]
    public async Task TheSameSpeakerTwiceOrAnUnknownSpeakerOrLineChangesNothing()
    {
        var id = await RecordingAsync();
        var before = (await LoadAsync(id)).Version;

        var same = await CallAsync(new { recordingId = id, speakerId = "spk1", otherSpeakerId = "spk1" });
        var speaker = await CallAsync(new { recordingId = id, speakerId = "spk1", otherSpeakerId = "spk9" });
        var line = await CallAsync(new { recordingId = id, speakerId = "spk1", otherSpeakerId = "spk2", fromSegmentId = "s9999" });

        Assert.Equal("bridge.invalidParams", same.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Choose two different speakers.", same.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("transcript.speakerNotFound", speaker.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("spk9", speaker.GetProperty("error").GetProperty("detail").GetString());
        Assert.Equal("transcript.segmentNotFound", line.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("s9999", line.GetProperty("error").GetProperty("detail").GetString());
        Assert.Equal(before, (await LoadAsync(id)).Version);
    }

    [Fact]
    public async Task NoLineToSwapWritesNothing()
    {
        var id = await RecordingAsync();
        // A third speaker with no lines: swapping it with Speaker 2 from the last line (Speaker 1's) touches no line.
        await _host.CallAsync("transcript.setSegmentSpeaker", JsonSerializer.Serialize(new { recordingId = id, segmentId = "s0002", speakerId = (string?)null, newSpeakerName = "Ana" }));
        await _host.CallAsync("transcript.setSegmentSpeaker", JsonSerializer.Serialize(new { recordingId = id, segmentId = "s0002", speakerId = "spk2" }));
        var before = await LoadAsync(id);
        Assert.Contains(before.Speakers, s => s.Id == "spk3" && s.Name == "Ana");

        var result = (await CallAsync(new { recordingId = id, speakerId = "spk3", otherSpeakerId = "spk2", fromSegmentId = "s0003" })).GetProperty("result");

        Assert.Empty(result.GetProperty("segments").EnumerateArray());
        Assert.Equal(["spk1", "spk2", "spk3"], result.GetProperty("speakers").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(before.Version, (await LoadAsync(id)).Version);
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.DoesNotContain(history, h => h.Summary == "Speakers swapped");
    }
}
