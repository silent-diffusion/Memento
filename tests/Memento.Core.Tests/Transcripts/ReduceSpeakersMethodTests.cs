using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><c>transcript.reduceSpeakers</c> and its Undo, <c>transcript.restoreSpeakers</c>, through the bridge.</summary>
public sealed class ReduceSpeakersMethodTests : IDisposable
{
    private static readonly string[] FirstLine = ["s1"];
    private static readonly string[] UnknownLine = ["s99"];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// Four speakers in order of first appearance; voices A (spk1, spk3) and B (spk2, spk4): the diarizer split two
    /// people into four. spk1 talks most, then spk2, spk3, spk4.
    /// </summary>
    private static TranscriptDocument FourSpeakers() => Document(
        Segment("s1", 0, 10, "One.", "spk1"),
        Segment("s2", 10, 18, "Two.", "spk2"),
        Segment("s3", 18, 24, "Three.", "spk3"),
        Segment("s4", 24, 28, "Four.", "spk4"),
        Segment("s5", 28, 32, "Five.", "spk1"));

    private static VoicesDocument Voices() => new(
        VoicesDocument.CurrentSchemaVersion,
        "nemo-titanet-small",
        "test",
        [],
        [
            new VoiceCluster("mic", 0, [1f, 0f, 0.1f], 14, ["s1", "s5"]),
            new VoiceCluster("mic", 1, [0f, 1f, 0.1f], 8, ["s2"]),
            new VoiceCluster("mic", 2, [0.95f, 0.05f, 0f], 6, ["s3"]),
            new VoiceCluster("mic", 3, [0.05f, 0.9f, 0f], 4, ["s4"]),
        ]);

    private async Task<string> RecordingAsync(VoicesDocument? voices)
    {
        var id = await _host.RecordAsync("Planning meeting", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => FourSpeakers(), CancellationToken.None);
        if (voices is not null)
        {
            await _host.Transcripts.SaveVoicesAsync(id, voices, CancellationToken.None);
        }

        return id;
    }

    private Task<JsonElement> ResultAsync(string method, object parameters) => _host.ResultAsync(method, JsonSerializer.Serialize(parameters));

    private async Task<string> ErrorAsync(string method, object parameters)
    {
        var response = await _host.CallAsync(method, JsonSerializer.Serialize(parameters));
        return response.GetProperty("error").GetProperty("code").GetString()!;
    }

    private async Task<TranscriptDocument> TranscriptAsync(string id) => (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task TheMostAlikeVoicesAreMergedIntoTheSpeakerWithMoreTalkTime()
    {
        var id = await RecordingAsync(Voices());

        var result = await ResultAsync("transcript.reduceSpeakers", new { recordingId = id, count = 2 });

        Assert.Equal("voices", result.GetProperty("basis").GetString());
        Assert.Equal(2, result.GetProperty("segmentsChanged").GetInt32());
        Assert.Equal(["spk1", "spk2"], result.GetProperty("speakers").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        var merged = result.GetProperty("merged").EnumerateArray().Select(m => (m.GetProperty("speaker").GetProperty("id").GetString(), m.GetProperty("intoSpeakerId").GetString())).ToList();
        Assert.Equal([("spk3", "spk1"), ("spk4", "spk2")], merged);
        var after = await TranscriptAsync(id);
        Assert.Equal(["spk1", "spk2", "spk1", "spk2", "spk1"], after.Segments.Select(s => s.Speaker));
        Assert.Equal([20_000L, 12_000L], after.Speakers.Select(s => s.TalkTimeMs));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "edited" && h.Summary == "Speakers reduced" && h.Detail == "2 speakers left, 2 speakers merged by voice, 2 lines moved");
    }

    [Fact]
    public async Task RestoringTheMergesLastFirstPutsEverythingBackInOneWrite()
    {
        var id = await RecordingAsync(Voices());
        var before = await TranscriptAsync(id);
        var result = await ResultAsync("transcript.reduceSpeakers", new { recordingId = id, count = 1 });
        var merges = result.GetProperty("merged").EnumerateArray().Reverse().Select(m => new { speaker = m.GetProperty("speaker"), segmentIds = m.GetProperty("segmentIds") }).ToList();
        var version = (await TranscriptAsync(id)).Version;

        var restored = await ResultAsync("transcript.restoreSpeakers", new { recordingId = id, speakers = merges });

        var after = await TranscriptAsync(id);
        Assert.Equal(version + 1, after.Version);
        Assert.Equal(before.Segments.Select(s => s.Speaker), after.Segments.Select(s => s.Speaker));
        Assert.Equal(
            before.Speakers.Select(s => (s.Id, s.Name, s.Color, s.Renamed, s.TalkTimeMs)).Order(),
            after.Speakers.Select(s => (s.Id, s.Name, s.Color, s.Renamed, s.TalkTimeMs)).Order());
        Assert.Equal(4, restored.GetProperty("speakers").GetArrayLength());
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "edited" && h.Summary == "Speakers restored");
    }

    [Fact]
    public async Task WithoutVoicesTheLeastSpeechGoesFirstIntoTheMost()
    {
        var id = await RecordingAsync(null);

        var result = await ResultAsync("transcript.reduceSpeakers", new { recordingId = id, count = 3 });

        Assert.Equal("talkTime", result.GetProperty("basis").GetString());
        var merge = Assert.Single(result.GetProperty("merged").EnumerateArray());
        Assert.Equal("spk4", merge.GetProperty("speaker").GetProperty("id").GetString());
        Assert.Equal("spk1", merge.GetProperty("intoSpeakerId").GetString());
        Assert.Equal(["s4"], merge.GetProperty("segmentIds").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task NamedSpeakersAreNeverMergedWithEachOtherAndKeepTheirNames()
    {
        var id = await RecordingAsync(Voices());
        await ResultAsync("transcript.renameSpeaker", new { recordingId = id, speakerId = "spk3", name = "Avery Stone" });
        await ResultAsync("transcript.renameSpeaker", new { recordingId = id, speakerId = "spk2", name = "Rowan Hale" });

        var result = await ResultAsync("transcript.reduceSpeakers", new { recordingId = id, count = 1 });

        // spk1 sounds like Avery (spk3) and goes into the named speaker; spk4 into Rowan. Two named people are left.
        Assert.Equal(["Rowan Hale", "Avery Stone"], result.GetProperty("speakers").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        var after = await TranscriptAsync(id);
        Assert.Equal(["spk3", "spk2", "spk3", "spk2", "spk3"], after.Segments.Select(s => s.Speaker));
    }

    [Fact]
    public async Task NothingToReduceWritesNothing()
    {
        var id = await RecordingAsync(Voices());
        var version = (await TranscriptAsync(id)).Version;

        var result = await ResultAsync("transcript.reduceSpeakers", new { recordingId = id, count = 4 });

        Assert.Equal(0, result.GetProperty("merged").GetArrayLength());
        Assert.Equal(4, result.GetProperty("speakers").GetArrayLength());
        Assert.Equal(version, (await TranscriptAsync(id)).Version);
    }

    [Fact]
    public async Task BadCountsAndUnknownLinesAreRefusedWithoutChangingAnything()
    {
        var id = await RecordingAsync(Voices());
        var version = (await TranscriptAsync(id)).Version;
        var speaker = new { id = "spk9", name = "Avery", color = 2, renamed = true };

        Assert.Equal("bridge.invalidParams", await ErrorAsync("transcript.reduceSpeakers", new { recordingId = id, count = 0 }));
        Assert.Equal("bridge.invalidParams", await ErrorAsync("transcript.reduceSpeakers", new { recordingId = id, count = 21 }));
        Assert.Equal("bridge.invalidParams", await ErrorAsync("transcript.restoreSpeakers", new { recordingId = id, speakers = Array.Empty<object>() }));
        Assert.Equal(
            "transcript.segmentNotFound",
            await ErrorAsync("transcript.restoreSpeakers", new { recordingId = id, speakers = new[] { new { speaker, segmentIds = FirstLine }, new { speaker, segmentIds = UnknownLine } } }));
        Assert.Equal(
            "bridge.invalidParams",
            await ErrorAsync("transcript.restoreSpeakers", new { recordingId = id, speakers = new[] { new { speaker = new { id = "spk9", name = "Avery", color = 9, renamed = true }, segmentIds = FirstLine } } }));
        Assert.Equal("project.notFound", await ErrorAsync("transcript.reduceSpeakers", new { recordingId = "20261006-100000-aaaaaa", count = 2 }));

        var after = await TranscriptAsync(id);
        Assert.Equal(version, after.Version);
        Assert.DoesNotContain(after.Speakers, s => s.Id == "spk9");
    }

    [Fact]
    public void ReducingAddsNoNewIdsAndKeepsColours()
    {
        var transcript = FourSpeakers();

        var reduced = SpeakerReducer.Reduce(transcript.Segments, transcript.Speakers, Voices(), 2);

        Assert.All(reduced.Speakers, s => Assert.Contains(transcript.Speakers, t => t.Id == s.Id && t.Color == s.Color && t.Name == s.Name));
        Assert.Equal(SpeakerReducer.BasisVoices, reduced.Basis);
        Assert.Equal([new SpeakerRestore("spk3", "Speaker 3", 3, false), new SpeakerRestore("spk4", "Speaker 4", 4, false)], reduced.Merges.Select(m => m.Speaker));
    }
}
