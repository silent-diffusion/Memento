using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using Memento.Core.Voices;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Voices;

/// <summary>The <c>voices.*</c> methods through the bridge: enrolment, Undo, matching on a later recording, decline, forget.</summary>
public sealed class KnownVoicesMethodTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>spk1 (24 s) sounds along x, spk2 (16 s) along y, and spk3 (4 s) along z.</summary>
    private async Task<string> RecordingAsync(string title)
    {
        var id = await _host.RecordAsync(title, 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(
            id,
            TranscriptChangeReasons.Transcribed,
            _ => Document(
                Segment("s1", 0, 12, "Good morning, let us start.", "spk1"),
                Segment("s2", 12, 28, "Thanks, the numbers are in.", "spk2"),
                Segment("s3", 28, 40, "Good, then we can decide.", "spk1"),
                Segment("s4", 40, 44, "Agreed.", "spk3")),
            CancellationToken.None);
        await _host.Transcripts.SaveVoicesAsync(
            id,
            new VoicesDocument(VoicesDocument.CurrentSchemaVersion, "nemo-titanet-small", "test", [], [
                new VoiceCluster("mic", 0, [1f, 0.05f, 0f], 24, ["s1", "s3"]),
                new VoiceCluster("mic", 1, [0.05f, 1f, 0f], 16, ["s2"]),
                new VoiceCluster("mic", 2, [0f, 0f, 1f], 4, ["s4"]),
            ]),
            CancellationToken.None);
        return id;
    }

    private Task<JsonElement> ResultAsync(string method, object parameters) => _host.ResultAsync(method, JsonSerializer.Serialize(parameters));

    private async Task<string> ErrorAsync(string method, object parameters)
    {
        var response = await _host.CallAsync(method, JsonSerializer.Serialize(parameters));
        return response.GetProperty("error").GetProperty("code").GetString()!;
    }

    private Task<JsonElement> RememberOnAsync(bool on = true) => _host.ResultAsync("settings.set", on ? """{"speakers":{"rememberVoices":true}}""" : """{"speakers":{"rememberVoices":false}}""");

    private async Task NameAsync(string id, string speakerId, string name) =>
        await ResultAsync("transcript.renameSpeaker", new { recordingId = id, speakerId, name });

    private KnownVoicesStore Store => _host.Get<KnownVoicesStore>();

    [Fact]
    public async Task RememberingIsOffByDefaultAndSaysWhy()
    {
        var id = await RecordingAsync("Monday");
        await NameAsync(id, "spk1", "Ana");

        var result = await ResultAsync("voices.remember", new { recordingId = id, speakerId = "spk1" });

        Assert.False(result.GetProperty("remembered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("changeId").ValueKind);
        Assert.Contains("Remember speakers by voice", result.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Store.FilePath));
        Assert.Empty((await ResultAsync("voices.matches", new { recordingId = id })).GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task ANamedSpeakerIsLearnedAndUndoTakesItBack()
    {
        await RememberOnAsync();
        var id = await RecordingAsync("Monday");
        await NameAsync(id, "spk1", "Ana");

        var result = await ResultAsync("voices.remember", new { recordingId = id, speakerId = "spk1" });

        Assert.True(result.GetProperty("remembered").GetBoolean());
        Assert.Equal("Ana", result.GetProperty("voice").GetProperty("name").GetString());
        Assert.Equal(1, result.GetProperty("voice").GetProperty("recordings").GetInt32());
        var list = await _host.ResultAsync("voices.list");
        Assert.True(list.GetProperty("remember").GetBoolean());
        Assert.Equal("Ana", list.GetProperty("voices")[0].GetProperty("name").GetString());
        var stored = Assert.Single((await Store.LoadAsync(CancellationToken.None)).Voices);
        Assert.Equal(24, Assert.Single(stored.Samples).Seconds);

        await ResultAsync("voices.revert", new { changeId = result.GetProperty("changeId").GetString() });

        Assert.Empty((await Store.LoadAsync(CancellationToken.None)).Voices);
        Assert.Equal("voices.notFound", await ErrorAsync("voices.revert", new { changeId = result.GetProperty("changeId").GetString() }));
    }

    [Fact]
    public async Task TooLittleSpeechOrNoNameIsNotRemembered()
    {
        await RememberOnAsync();
        var id = await RecordingAsync("Monday");

        var unnamed = await ResultAsync("voices.remember", new { recordingId = id, speakerId = "spk2" });
        await NameAsync(id, "spk3", "Cy");
        var short4 = await ResultAsync("voices.remember", new { recordingId = id, speakerId = "spk3" });

        Assert.Contains("has no name yet", unnamed.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal("Cy speaks for 4 s with a usable voice here; Memento learns a voice from 10 s or more, so it was not remembered.", short4.GetProperty("reason").GetString());
        Assert.Equal("transcript.speakerNotFound", await ErrorAsync("voices.remember", new { recordingId = id, speakerId = "spk9" }));
    }

    [Fact]
    public async Task NamingTheSameSpeakerAgainMovesItsVoiceToTheNewName()
    {
        await RememberOnAsync();
        var id = await RecordingAsync("Monday");
        await NameAsync(id, "spk1", "Ana");
        await ResultAsync("voices.remember", new { recordingId = id, speakerId = "spk1" });
        await NameAsync(id, "spk1", "Anna");

        var second = await ResultAsync("voices.remember", new { recordingId = id, speakerId = "spk1" });

        Assert.Equal(["Anna"], (await Store.LoadAsync(CancellationToken.None)).Voices.Select(v => v.Name));

        // Undo brings Ana back with her confirmation.
        await ResultAsync("voices.revert", new { changeId = second.GetProperty("changeId").GetString() });
        Assert.Equal(["Ana"], (await Store.LoadAsync(CancellationToken.None)).Voices.Select(v => v.Name));
    }

    [Fact]
    public async Task ALaterRecordingSuggestsTheKnownVoiceAndUseNameRefinesIt()
    {
        await RememberOnAsync();
        var monday = await RecordingAsync("Monday");
        await NameAsync(monday, "spk1", "Ana");
        await ResultAsync("voices.remember", new { recordingId = monday, speakerId = "spk1" });
        var tuesday = await RecordingAsync("Tuesday");

        var matches = (await ResultAsync("voices.matches", new { recordingId = tuesday })).GetProperty("matches");

        var match = Assert.Single(matches.EnumerateArray());
        Assert.Equal("spk1", match.GetProperty("speakerId").GetString());
        Assert.Equal("Ana", match.GetProperty("name").GetString());
        Assert.InRange(match.GetProperty("similarity").GetDouble(), 0.99, 1.0);
        Assert.Equal(1, match.GetProperty("recordings").GetInt32());

        var accepted = await ResultAsync("voices.acceptMatch", new { recordingId = tuesday, speakerId = "spk1", voiceId = match.GetProperty("voiceId").GetString() });

        Assert.Contains(accepted.GetProperty("speakers").EnumerateArray(), s => s.GetProperty("name").GetString() == "Ana" && s.GetProperty("renamed").GetBoolean());
        var voice = Assert.Single((await Store.LoadAsync(CancellationToken.None)).Voices);
        Assert.Equal([monday, tuesday], voice.Recordings);
        Assert.Equal(2, voice.Samples.Count);
        Assert.Empty((await ResultAsync("voices.matches", new { recordingId = tuesday })).GetProperty("matches").EnumerateArray());

        // Undo of Use name: the speaker's old name back (transcript.restoreSpeaker) and the refinement reverted.
        await ResultAsync("voices.revert", new { changeId = accepted.GetProperty("changeId").GetString() });
        Assert.Equal([monday], Assert.Single((await Store.LoadAsync(CancellationToken.None)).Voices).Recordings);
    }

    [Fact]
    public async Task NotNameHidesTheSuggestionInThisRecordingOnly()
    {
        await RememberOnAsync();
        var monday = await RecordingAsync("Monday");
        await NameAsync(monday, "spk1", "Ana");
        var voiceId = (await ResultAsync("voices.remember", new { recordingId = monday, speakerId = "spk1" })).GetProperty("voice").GetProperty("id").GetString();
        var tuesday = await RecordingAsync("Tuesday");
        var wednesday = await RecordingAsync("Wednesday");

        var declined = await ResultAsync("voices.decline", new { recordingId = tuesday, voiceId, declined = true });

        Assert.Empty(declined.GetProperty("matches").EnumerateArray());
        Assert.Single((await ResultAsync("voices.matches", new { recordingId = wednesday })).GetProperty("matches").EnumerateArray());
        var undone = await ResultAsync("voices.decline", new { recordingId = tuesday, voiceId, declined = false });
        Assert.Single(undone.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task SuggestOffForgetAndForgetAll()
    {
        await RememberOnAsync();
        var monday = await RecordingAsync("Monday");
        await NameAsync(monday, "spk1", "Ana");
        await NameAsync(monday, "spk2", "Ben");
        var ana = (await ResultAsync("voices.remember", new { recordingId = monday, speakerId = "spk1" })).GetProperty("voice").GetProperty("id").GetString();
        var ben = await ResultAsync("voices.remember", new { recordingId = monday, speakerId = "spk2" });
        var tuesday = await RecordingAsync("Tuesday");

        var muted = await ResultAsync("voices.setSuggest", new { voiceId = ana, suggest = false });
        Assert.False(muted.GetProperty("voices").EnumerateArray().First(v => v.GetProperty("id").GetString() == ana).GetProperty("suggest").GetBoolean());
        Assert.Equal(["Ben"], (await ResultAsync("voices.matches", new { recordingId = tuesday })).GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("name").GetString()));

        var forgot = await ResultAsync("voices.forget", new { voiceId = ana });
        Assert.Equal(["Ben"], forgot.GetProperty("voices").EnumerateArray().Select(v => v.GetProperty("name").GetString()));
        Assert.Equal("voices.notFound", await ErrorAsync("voices.forget", new { voiceId = ana }));

        await _host.ResultAsync("voices.forgetAll");
        Assert.Empty((await _host.ResultAsync("voices.list")).GetProperty("voices").EnumerateArray());
        Assert.False(File.Exists(Store.FilePath));

        // A forgotten voice never comes back through Undo.
        Assert.Equal("voices.notFound", await ErrorAsync("voices.revert", new { changeId = ben.GetProperty("changeId").GetString() }));
    }

    [Fact]
    public async Task DeletingARecordingTakesItsConfirmationsAway()
    {
        await RememberOnAsync();
        var monday = await RecordingAsync("Monday");
        await NameAsync(monday, "spk1", "Ana");
        await ResultAsync("voices.remember", new { recordingId = monday, speakerId = "spk1" });

        await ResultAsync("project.delete", new { recordingId = monday });

        Assert.Empty((await Store.LoadAsync(CancellationToken.None)).Voices);
    }

    [Fact]
    public async Task AKnownVoicesFileFromANewerMementoIsReported()
    {
        await RememberOnAsync();
        var id = await RecordingAsync("Monday");
        Directory.CreateDirectory(Path.GetDirectoryName(Store.FilePath)!);
        await File.WriteAllTextAsync(Store.FilePath, """{ "schemaVersion": 9, "voices": [] }""");

        var response = await _host.CallAsync("voices.list");

        Assert.Equal("voices.newerVersion", response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("voices.newerVersion", await ErrorAsync("voices.matches", new { recordingId = id }));
    }

    [Fact]
    public async Task ParametersAreCheckedStrictly()
    {
        Assert.Equal("bridge.invalidParams", await ErrorAsync("voices.forget", new { voiceId = "v1", extra = 1 }));
        Assert.Equal("bridge.invalidParams", await ErrorAsync("voices.setSuggest", new { voiceId = "v1" }));
        Assert.Equal("bridge.invalidParams", await ErrorAsync("voices.decline", new { recordingId = "x", voiceId = "v1" }));
        Assert.Equal("project.notFound", await ErrorAsync("voices.matches", new { recordingId = "20260101-000000-aaaaaa" }));
        Assert.Equal("voices.notFound", await ErrorAsync("voices.setSuggest", new { voiceId = "v0000000000", suggest = true }));
    }
}
