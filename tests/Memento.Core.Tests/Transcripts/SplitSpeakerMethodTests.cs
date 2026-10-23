using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><c>transcript.splitSpeaker</c> (2.6.0) through the bridge: a speaker's voices grouped into two, the smaller half a new speaker.</summary>
public sealed class SplitSpeakerMethodTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>The Meeting fixture: s0001 (4 s) and s0003 (5.5 s) are Speaker 1's, s0002 Speaker 2's; two voices behind Speaker 1.</summary>
    private static VoicesDocument Voices() => new(
        VoicesDocument.CurrentSchemaVersion,
        "nemo-titanet-small",
        "sig",
        [],
        [
            new VoiceCluster("mic", 0, [1f, 0f], 10, ["s0001"]),
            new VoiceCluster("mic", 1, [0f, 1f], 10, ["s0003"]),
            new VoiceCluster("mic", 2, [0.7f, 0.7f], 4, ["s0002"]),
        ]);

    private async Task<string> RecordingAsync(bool withVoices = true)
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => Meeting(), CancellationToken.None);
        if (withVoices)
        {
            await _host.Transcripts.SaveVoicesAsync(id, Voices(), CancellationToken.None);
        }

        return id;
    }

    private Task<JsonElement> ResultAsync(object parameters) => _host.ResultAsync("transcript.splitSpeaker", JsonSerializer.Serialize(parameters));

    private async Task<(string Code, string? Detail, string Message)> ErrorAsync(object parameters)
    {
        var response = await _host.CallAsync("transcript.splitSpeaker", JsonSerializer.Serialize(parameters));
        var error = response.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.TryGetProperty("detail", out var detail) ? detail.GetString() : null, error.GetProperty("message").GetString()!);
    }

    private async Task<TranscriptDocument> LoadAsync(string id) => (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task TheSmallerHalfBecomesANewSpeakerInOneWrite()
    {
        var id = await RecordingAsync();
        var before = (await LoadAsync(id)).Version;

        var result = await ResultAsync(new { recordingId = id, speakerId = "spk1" });

        Assert.Equal(["s0001"], result.GetProperty("segmentIds").EnumerateArray().Select(e => e.GetString()));
        var created = result.GetProperty("created");
        Assert.Equal(("spk3", "Speaker 3", false, 3, 4000L), (created.GetProperty("id").GetString(), created.GetProperty("name").GetString(), created.GetProperty("renamed").GetBoolean(), created.GetProperty("color").GetInt32(), created.GetProperty("talkTimeMs").GetInt64()));
        Assert.Equal(["spk1", "spk2", "spk3"], result.GetProperty("speakers").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        var after = await LoadAsync(id);
        Assert.Equal(before + 1, after.Version);
        Assert.Equal(["spk3", "spk2", "spk1"], after.Segments.Select(s => s.Speaker));
        Assert.Equal([5_500L, 4_500L, 4_000L], after.Speakers.Select(s => s.TalkTimeMs));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Speaker split" && h.Detail == "Speaker 1 · 1 line to Speaker 3 by voice");
    }

    [Fact]
    public async Task MergingTheNewSpeakerBackIsTheUndo()
    {
        var id = await RecordingAsync();
        await ResultAsync(new { recordingId = id, speakerId = "spk1" });

        await _host.ResultAsync("transcript.mergeSpeakers", JsonSerializer.Serialize(new { recordingId = id, fromSpeakerId = "spk3", intoSpeakerId = "spk1" }));

        var after = await LoadAsync(id);
        Assert.Equal(["spk1", "spk2"], after.Speakers.Select(s => s.Id));
        Assert.Equal(["spk1", "spk2", "spk1"], after.Segments.Select(s => s.Speaker));
    }

    [Fact]
    public async Task ASpeakerHeardAsOneVoiceIsRefusedWithNothingWritten()
    {
        var id = await RecordingAsync();
        var before = (await LoadAsync(id)).Version;

        var (code, detail, message) = await ErrorAsync(new { recordingId = id, speakerId = "spk2" });

        Assert.Equal(("transcript.cannotSplit", "spk2"), (code, detail));
        Assert.Equal("Speaker 2 was heard as one voice, so there is nothing to split by sound. Move lines with their menu instead. Nothing was changed.", message);
        Assert.Equal(before, (await LoadAsync(id)).Version);
    }

    [Fact]
    public async Task VoicesThatSoundAlikeAreRefusedWithTheCosine()
    {
        var id = await RecordingAsync(withVoices: false);
        var alike = new VoicesDocument(VoicesDocument.CurrentSchemaVersion, "nemo-titanet-small", "sig", [], [new VoiceCluster("mic", 0, [1f, 0f], 10, ["s0001"]), new VoiceCluster("mic", 1, [0.98f, 0.2f], 10, ["s0003"])]);
        await _host.Transcripts.SaveVoicesAsync(id, alike, CancellationToken.None);

        var (code, _, message) = await ErrorAsync(new { recordingId = id, speakerId = "spk1" });

        Assert.Equal("transcript.cannotSplit", code);
        Assert.StartsWith("Speaker 1's voices sound alike (0.98), so Memento cannot split them by sound.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherRefusalsNameWhatIsWrong()
    {
        var withoutVoices = await RecordingAsync(withVoices: false);
        Assert.Equal("transcript.cannotSplit", (await ErrorAsync(new { recordingId = withoutVoices, speakerId = "spk1" })).Code);

        var id = await RecordingAsync();
        var (code, detail, _) = await ErrorAsync(new { recordingId = id, speakerId = "spk9" });
        Assert.Equal(("transcript.speakerNotFound", "spk9"), (code, detail));
        Assert.Equal("project.notFound", (await ErrorAsync(new { recordingId = "20260101-000000-nope", speakerId = "spk1" })).Code);

        var empty = await _host.RecordAsync("Planning", 1, Mic);
        Assert.Equal("transcript.none", (await ErrorAsync(new { recordingId = empty, speakerId = "spk1" })).Code);
    }
}
