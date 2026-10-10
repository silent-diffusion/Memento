using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Tests.Bridge;

/// <summary>
/// Pins the JSON of every 2.0 Review result (docs/BRIDGE.md › "Review (2.0)") and the strict reading of its parameters.
/// If one fails, update <c>ui/src/bridge/types.ts</c> in the same change.
/// </summary>
public sealed class ReviewContractSerializationTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1));

    private static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    [Fact]
    public void KnownVoices()
    {
        Assert.Equal(
            """{"remember":true,"voices":[{"id":"v0a1b2c3d4e","name":"Priya Natarajan","recordings":4,"lastConfirmedAt":"2026-10-06T10:00:00+01:00","suggest":false}]}""",
            Json(new KnownVoicesResult(true, [new KnownVoiceInfo("v0a1b2c3d4e", "Priya Natarajan", 4, At, false)]), ReviewBridgeJsonContext.Default.KnownVoicesResult));
        Assert.Equal(
            """{"remembered":false,"changeId":null,"voice":null,"reason":"Off."}""",
            Json(new VoiceRememberResult(false, null, null, "Off."), ReviewBridgeJsonContext.Default.VoiceRememberResult));
        Assert.Equal(
            """{"matches":[{"speakerId":"spk2","voiceId":"v1","name":"Priya Natarajan","similarity":0.912,"recordings":4}]}""",
            Json(new VoiceMatchesResult([new VoiceMatch("spk2", "v1", "Priya Natarajan", 0.912, 4)]), ReviewBridgeJsonContext.Default.VoiceMatchesResult));
        Assert.Equal(
            """{"speakers":[{"id":"spk2","name":"Priya Natarajan","renamed":true,"color":2,"talkTimeMs":1000}],"changeId":"c1"}""",
            Json(new VoiceAcceptResult([new Speaker("spk2", "Priya Natarajan", true, 2, 1000)], "c1"), ReviewBridgeJsonContext.Default.VoiceAcceptResult));
    }

    [Fact]
    public void SuggestedChaptersAndSeveralLines()
    {
        Assert.Equal(
            """{"suggestions":[{"id":"sc760000","atMs":760000,"title":"Status pill density","basis":"The subject changes \u00B7 4 s pause"}]}""",
            Json(new ChapterSuggestionsResult([new ChapterSuggestion("sc760000", 760_000, "Status pill density", "The subject changes · 4 s pause")]), ReviewBridgeJsonContext.Default.ChapterSuggestionsResult));
        var line = new TranscriptSegment("s1", 1, 2, "mic", "spk1", 1, "Hi.", 0.9, [], null);
        Assert.Equal(
            """{"segments":[{"id":"s1","start":1,"end":2,"track":"mic","speaker":"spk1","speakerConfidence":1,"text":"Hi.","confidence":0.9,"words":[],"edited":null}],"speakers":[]}""",
            Json(new SegmentsSpeakersResult([line], []), ReviewBridgeJsonContext.Default.SegmentsSpeakersResult));
    }

    [Theory]
    [InlineData("""{"voiceId":"v1","suggest":true}""", true)]
    [InlineData("""{"voiceId":"v1"}""", false)]
    [InlineData("""{"voiceId":"v1","suggest":true,"name":"x"}""", false)]
    public void VoiceSuggestParamsAreStrict(string json, bool valid)
    {
        if (valid)
        {
            Assert.True(JsonSerializer.Deserialize(json, ReviewBridgeJsonContext.Default.VoiceSuggestParams)!.Suggest);
        }
        else
        {
            Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, ReviewBridgeJsonContext.Default.VoiceSuggestParams));
        }
    }

    [Fact]
    public void SeveralLinesParamsReadTheNewSpeakerName()
    {
        var parameters = JsonSerializer.Deserialize("""{"recordingId":"r","segmentIds":["s1","s2"],"speakerId":null,"newSpeakerName":"Ana"}""", ReviewBridgeJsonContext.Default.TranscriptSetSegmentsSpeakerParams)!;

        Assert.Equal(["s1", "s2"], parameters.SegmentIds);
        Assert.Equal("Ana", parameters.NewSpeakerName);
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize("""{"recordingId":"r"}""", ReviewBridgeJsonContext.Default.TranscriptSetSegmentsSpeakerParams));
    }
}
