using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Transcripts;

/// <summary>The <c>transcript.*</c> methods through the bridge, on a recorded project with a synthetic transcript.</summary>
public sealed class TranscriptMethodTests : IDisposable
{
    private static readonly string[] KnownAndUnknownLine = ["s0001", "s9999"];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<string> RecordingWithTranscriptAsync(TranscriptDocument? transcript = null)
    {
        var id = await _host.RecordAsync("Planning meeting", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => transcript ?? TranscriptFixtures.Meeting(), CancellationToken.None);
        _host.Sink.Clear();
        return id;
    }

    private Task<JsonElement> ResultAsync(string method, object parameters) => _host.ResultAsync(method, JsonSerializer.Serialize(parameters));

    private async Task<string> ErrorAsync(string method, object parameters)
    {
        var response = await _host.CallAsync(method, JsonSerializer.Serialize(parameters));
        return response.GetProperty("error").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task GetReturnsNullAndNoneBeforeTranscription()
    {
        var id = await _host.RecordAsync("Plain", 1, Mic);

        var result = await ResultAsync("transcript.get", new { recordingId = id });

        Assert.Equal(JsonValueKind.Null, result.GetProperty("transcript").ValueKind);
        Assert.Equal("none", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("failure").ValueKind);
    }

    [Fact]
    public async Task GetReturnsTheTranscriptAndDone()
    {
        var id = await RecordingWithTranscriptAsync();

        var result = await ResultAsync("transcript.get", new { recordingId = id });

        Assert.Equal("done", result.GetProperty("status").GetString());
        var transcript = result.GetProperty("transcript");
        Assert.Equal(1, transcript.GetProperty("version").GetInt32());
        Assert.Equal(3, transcript.GetProperty("segments").GetArrayLength());
        Assert.Equal(2, transcript.GetProperty("speakers").GetArrayLength());
    }

    [Fact]
    public async Task EditingKeepsTheFirstOriginalRealignsWordsAndBumpsTheVersion()
    {
        var id = await RecordingWithTranscriptAsync();

        await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0002", text = "Thanks. Let us review the budget." });
        var result = await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0002", text = "Thanks. Let us review the whole budget." });

        Assert.Equal(3, result.GetProperty("version").GetInt32());
        var segment = result.GetProperty("segment");
        Assert.Equal("Thanks. Let us review the whole budget.", segment.GetProperty("text").GetString());
        Assert.Equal("Thanks. Let's review the budget first.", segment.GetProperty("edited").GetProperty("original").GetString());
        var words = segment.GetProperty("words").EnumerateArray().ToList();
        Assert.Equal(7, words.Count);
        Assert.Equal(4.5, words[0].GetProperty("s").GetDouble());
        Assert.Equal(9, words[^1].GetProperty("e").GetDouble());
        Assert.All(words, w => Assert.Equal(1, w.GetProperty("c").GetDouble()));
        var changed = await _host.Sink.WaitForAsync("transcript.changed", p => p.GetProperty("version").GetInt32() == 3);
        Assert.Equal("edited", changed.GetProperty("reason").GetString());
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Equal(2, history.Count(h => h.Stage == "edited" && h.Summary == "Transcript edited"));
    }

    [Fact]
    public async Task EditErrorsNameTheMissingThing()
    {
        var plain = await _host.RecordAsync("Plain", 1, Mic);
        var id = await RecordingWithTranscriptAsync();

        Assert.Equal("transcript.none", await ErrorAsync("transcript.editSegment", new { recordingId = plain, segmentId = "s0001", text = "x" }));
        Assert.Equal("transcript.segmentNotFound", await ErrorAsync("transcript.editSegment", new { recordingId = id, segmentId = "s9999", text = "x" }));
        Assert.Equal("project.notFound", await ErrorAsync("transcript.editSegment", new { recordingId = "20990101-000000-zzzzzz", segmentId = "s0001", text = "x" }));
        Assert.Equal("bridge.invalidParams", await ErrorAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0001", text = new string('x', 5000) }));
    }

    [Fact]
    public async Task SetSegmentSpeakerAssignsClearsAndCreatesSpeakers()
    {
        var id = await RecordingWithTranscriptAsync();

        var moved = await ResultAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0003", speakerId = "spk2" });
        Assert.Equal("spk2", moved.GetProperty("segment").GetProperty("speaker").GetString());
        Assert.Equal(1, moved.GetProperty("segment").GetProperty("speakerConfidence").GetDouble());
        var talk = moved.GetProperty("speakers").EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("talkTimeMs").GetInt64());
        Assert.Equal(4000, talk["spk1"]);
        Assert.Equal(10_000, talk["spk2"]);

        var created = await ResultAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", newSpeakerName = "Avery" });
        var avery = created.GetProperty("speakers").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Avery");
        Assert.Equal("spk3", avery.GetProperty("id").GetString());
        Assert.True(avery.GetProperty("renamed").GetBoolean());
        Assert.Equal(3, avery.GetProperty("color").GetInt32());
        Assert.Equal("spk3", created.GetProperty("segment").GetProperty("speaker").GetString());

        var cleared = await ResultAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", speakerId = (string?)null });
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("segment").GetProperty("speaker").ValueKind);
        Assert.Equal("transcript.speakerNotFound", await ErrorAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", speakerId = "spk9" }));
    }

    [Fact]
    public async Task RenamingASpeakerChangesItOnceAndTheLibraryFindsTheName()
    {
        var id = await RecordingWithTranscriptAsync();

        var result = await ResultAsync("transcript.renameSpeaker", new { recordingId = id, speakerId = "spk1", name = "Rowan Hale" });

        var speaker = result.GetProperty("speakers").EnumerateArray().First();
        Assert.Equal("Rowan Hale", speaker.GetProperty("name").GetString());
        Assert.True(speaker.GetProperty("renamed").GetBoolean());
        var transcript = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;
        Assert.Equal(2, transcript.Segments.Count(s => s.Speaker == "spk1"));
        var list = await _host.ResultAsync("library.list", """{"query":"rowan"}""");
        var row = Assert.Single(list.GetProperty("recordings").EnumerateArray());
        Assert.Contains("Rowan Hale", row.GetProperty("people").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("bridge.invalidParams", await ErrorAsync("transcript.renameSpeaker", new { recordingId = id, speakerId = "spk1", name = " " }));
    }

    [Fact]
    public async Task MergingMovesEveryLineAndRemovesTheSpeaker()
    {
        var id = await RecordingWithTranscriptAsync();

        var result = await ResultAsync("transcript.mergeSpeakers", new { recordingId = id, fromSpeakerId = "spk2", intoSpeakerId = "spk1" });

        Assert.Equal(1, result.GetProperty("segmentsChanged").GetInt32());
        var speaker = Assert.Single(result.GetProperty("speakers").EnumerateArray());
        Assert.Equal("spk1", speaker.GetProperty("id").GetString());
        Assert.Equal(14_000, speaker.GetProperty("talkTimeMs").GetInt64());
        Assert.Equal("bridge.invalidParams", await ErrorAsync("transcript.mergeSpeakers", new { recordingId = id, fromSpeakerId = "spk1", intoSpeakerId = "spk1" }));
    }

    [Fact]
    public async Task RestoringAMergedSpeakerPutsItBackWithItsIdColourAndLines()
    {
        var id = await RecordingWithTranscriptAsync();
        var before = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;
        var spk2 = before.Speakers.Single(s => s.Id == "spk2");
        var lines = before.Segments.Where(s => s.Speaker == "spk2").Select(s => s.Id).ToList();
        await ResultAsync("transcript.mergeSpeakers", new { recordingId = id, fromSpeakerId = "spk2", intoSpeakerId = "spk1" });

        var result = await ResultAsync(
            "transcript.restoreSpeaker",
            new { recordingId = id, speaker = new { id = spk2.Id, name = spk2.Name, color = spk2.Color, renamed = spk2.Renamed }, segmentIds = lines });

        Assert.Equal(lines.Count, result.GetProperty("segmentsChanged").GetInt32());
        var after = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;
        Assert.Equal(before.Speakers.Select(s => (s.Id, s.Name, s.Color, s.TalkTimeMs)), after.Speakers.Select(s => (s.Id, s.Name, s.Color, s.TalkTimeMs)));
        Assert.Equal(before.Segments.Select(s => s.Speaker), after.Segments.Select(s => s.Speaker));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "edited" && h.Summary == "Speaker restored");
    }

    [Fact]
    public async Task RestoringAnExistingSpeakerResetsItsNameAndRenamedFlag()
    {
        var id = await RecordingWithTranscriptAsync();
        await ResultAsync("transcript.renameSpeaker", new { recordingId = id, speakerId = "spk1", name = "Rowan Hale" });

        var result = await ResultAsync(
            "transcript.restoreSpeaker",
            new { recordingId = id, speaker = new { id = "spk1", name = "Speaker 1", color = 1, renamed = false }, segmentIds = Array.Empty<string>() });

        Assert.Equal(0, result.GetProperty("segmentsChanged").GetInt32());
        var speaker = result.GetProperty("speakers").EnumerateArray().First();
        Assert.Equal("Speaker 1", speaker.GetProperty("name").GetString());
        Assert.False(speaker.GetProperty("renamed").GetBoolean());
        Assert.True(speaker.GetProperty("talkTimeMs").GetInt64() > 0);
    }

    [Fact]
    public async Task RestoreSpeakerRefusesBadValuesAndUnknownLinesWithoutChangingAnything()
    {
        var id = await RecordingWithTranscriptAsync();
        var version = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Version;

        Assert.Equal(
            "transcript.segmentNotFound",
            await ErrorAsync("transcript.restoreSpeaker", new { recordingId = id, speaker = new { id = "spk7", name = "Avery", color = 2, renamed = true }, segmentIds = KnownAndUnknownLine }));
        Assert.Equal(
            "bridge.invalidParams",
            await ErrorAsync("transcript.restoreSpeaker", new { recordingId = id, speaker = new { id = "spk7", name = "Avery", color = 7, renamed = true }, segmentIds = Array.Empty<string>() }));
        Assert.Equal(
            "bridge.invalidParams",
            await ErrorAsync("transcript.restoreSpeaker", new { recordingId = id, speaker = new { id = "../x", name = "Avery", color = 2, renamed = true }, segmentIds = Array.Empty<string>() }));
        Assert.Equal(
            "bridge.invalidParams",
            await ErrorAsync("transcript.restoreSpeaker", new { recordingId = id, speaker = new { id = "spk7", name = " ", color = 2, renamed = true }, segmentIds = Array.Empty<string>() }));

        var after = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!;
        Assert.Equal(version, after.Version);
        Assert.DoesNotContain(after.Speakers, s => s.Id == "spk7");
    }

    [Fact]
    public async Task RemoveSpeakerRemovesOnlyASpeakerWithoutLines()
    {
        var id = await RecordingWithTranscriptAsync();
        await ResultAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", newSpeakerName = "Avery" });

        Assert.Equal("transcript.speakerInUse", await ErrorAsync("transcript.removeSpeaker", new { recordingId = id, speakerId = "spk3" }));
        await ResultAsync("transcript.setSegmentSpeaker", new { recordingId = id, segmentId = "s0001", speakerId = "spk1" });
        var result = await ResultAsync("transcript.removeSpeaker", new { recordingId = id, speakerId = "spk3" });

        Assert.Equal(["spk1", "spk2"], result.GetProperty("speakers").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal("transcript.speakerNotFound", await ErrorAsync("transcript.removeSpeaker", new { recordingId = id, speakerId = "spk3" }));
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "edited" && h.Summary == "Speaker removed");
    }

    [Fact]
    public async Task EditingBackToTheOriginalWordingLeavesTheLineUnedited()
    {
        var id = await RecordingWithTranscriptAsync();
        var original = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Segments.Single(s => s.Id == "s0002").Text;
        await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0002", text = "Thanks. Let us review the budget." });

        var result = await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0002", text = original });

        var segment = result.GetProperty("segment");
        Assert.Equal(original, segment.GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Null, segment.GetProperty("edited").ValueKind);
    }

    [Fact]
    public async Task MarkReviewedRoundTrips()
    {
        var id = await RecordingWithTranscriptAsync();

        var result = await ResultAsync("transcript.markReviewed", new { recordingId = id, reviewed = true });

        Assert.True(result.GetProperty("reviewed").GetBoolean());
        Assert.True((await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Reviewed);
        var history = await _host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Summary == "Marked as reviewed");
    }

    [Fact]
    public async Task SearchFindsWordStartsWithSnippets()
    {
        var id = await RecordingWithTranscriptAsync();

        var result = await ResultAsync("transcript.search", new { recordingId = id, query = "BUDGET" });

        var matches = result.GetProperty("matches").EnumerateArray().ToList();
        Assert.Equal(["s0002", "s0003"], matches.Select(m => m.GetProperty("segmentId").GetString()));
        Assert.Equal(4.5, matches[0].GetProperty("start").GetDouble());
        Assert.Contains("budget", matches[0].GetProperty("snippet").GetString(), StringComparison.Ordinal);
        Assert.Empty((await ResultAsync("transcript.search", new { recordingId = id, query = "udget" })).GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task VersionsListAndRestoreKeepTheReplacedTranscript()
    {
        var id = await RecordingWithTranscriptAsync();
        await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0001", text = "Welcome, all." });

        var versions = (await ResultAsync("transcript.versions", new { recordingId = id })).GetProperty("versions").EnumerateArray().ToList();
        var original = Assert.Single(versions);
        Assert.Equal("transcribed", original.GetProperty("reason").GetString());
        Assert.Equal("whisper.cpp whisper-small", original.GetProperty("engine").GetString());
        Assert.Equal(3, original.GetProperty("segments").GetInt32());

        var restored = await ResultAsync("transcript.restoreVersion", new { recordingId = id, versionId = original.GetProperty("id").GetString() });

        Assert.Equal("Welcome everyone to the planning meeting.", restored.GetProperty("transcript").GetProperty("segments")[0].GetProperty("text").GetString());
        Assert.Equal(3, restored.GetProperty("transcript").GetProperty("version").GetInt32());
        var after = (await ResultAsync("transcript.versions", new { recordingId = id })).GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(["edited", "transcribed"], after.Select(v => v.GetProperty("reason").GetString()));
        await _host.Sink.WaitForAsync("transcript.changed", p => p.GetProperty("reason").GetString() == "restored");
        Assert.Equal("transcript.versionNotFound", await ErrorAsync("transcript.restoreVersion", new { recordingId = id, versionId = "20200101T000000000Z" }));
    }

    [Fact]
    public async Task VersionsAreEmptyWhenHistoryIsOff()
    {
        var id = await RecordingWithTranscriptAsync();
        await _host.ResultAsync("settings.set", """{"history":{"keepVersions":false}}""");
        await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0001", text = "Welcome, all." });

        Assert.Empty((await ResultAsync("transcript.versions", new { recordingId = id })).GetProperty("versions").EnumerateArray());
    }

    [Fact]
    public async Task TheLibrarySearchMatchesTranscriptTextWithASnippet()
    {
        var id = await RecordingWithTranscriptAsync();

        var hit = Assert.Single((await _host.ResultAsync("library.list", """{"query":"quarter approved"}""")).GetProperty("recordings").EnumerateArray());
        Assert.Equal(id, hit.GetProperty("id").GetString());
        Assert.Contains("quarter", hit.GetProperty("matchSnippet").GetString(), StringComparison.Ordinal);

        // A title match carries no transcript snippet.
        await _host.RecordAsync("Thermodynamics lecture", 1, Mic);
        var byTitle = Assert.Single((await _host.ResultAsync("library.list", """{"query":"thermodynamics"}""")).GetProperty("recordings").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, byTitle.GetProperty("matchSnippet").ValueKind);
        Assert.Empty((await _host.ResultAsync("library.list", """{"query":"nonexistentword"}""")).GetProperty("recordings").EnumerateArray());

        // Edits are searchable at once, and the index rebuilt from the folders keeps them.
        await ResultAsync("transcript.editSegment", new { recordingId = id, segmentId = "s0001", text = "Welcome to the zeppelin review." });
        Assert.Single((await _host.ResultAsync("library.list", """{"query":"zeppelin"}""")).GetProperty("recordings").EnumerateArray());
        await _host.Index.RebuildAsync(CancellationToken.None);
        Assert.Single((await _host.ResultAsync("library.list", """{"query":"zeppelin"}""")).GetProperty("recordings").EnumerateArray());
    }

    [Fact]
    public async Task RetranscribeWithoutTheStageRegisteredSaysTheEngineIsUnavailable()
    {
        var id = await RecordingWithTranscriptAsync();

        Assert.Equal("engine.unavailable", await ErrorAsync("transcript.retranscribe", new { recordingId = id }));
        Assert.Equal("models.notFound", await ErrorAsync("transcript.retranscribe", new { recordingId = id, modelId = "nope" }));
        Assert.Equal("engine.unavailable", await ErrorAsync("transcript.retranscribe", new { recordingId = id, modelId = "whisper-small" }));
    }

    [Fact]
    public async Task RetranscribeIsRefusedWhileRecording()
    {
        var (sessionId, id) = await _host.StartAsync("Live", Mic);

        Assert.Equal("project.recording", await ErrorAsync("transcript.retranscribe", new { recordingId = id }));
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _host.Recordings.WhenIdleAsync();
    }
}
