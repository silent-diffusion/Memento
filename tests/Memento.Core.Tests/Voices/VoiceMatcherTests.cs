using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Voices;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Voices;

/// <summary><see cref="VoiceMatcher"/>: the thresholds, the margin, preferred names and one speaker per voice.</summary>
public sealed class VoiceMatcherTests
{
    private const string Model = "nemo-titanet-small";

    /// <summary>A unit direction at <paramref name="degrees"/> from the x axis in the x-y plane (cosine with x is cos(degrees)).</summary>
    private static float[] At(double degrees) => [(float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180), 0f];

    /// <summary>Two speakers, 20 s each: spk1's voice along <paramref name="first"/>, spk2's along <paramref name="second"/>.</summary>
    private static (IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers, VoicesDocument Voices) Recording(float[] first, float[] second, bool firstNamed = false)
    {
        var document = Document(
            Segment("s1", 0, 20, "One.", "spk1"),
            Segment("s2", 20, 40, "Two.", "spk2"));
        var speakers = document.Speakers.Select(s => s.Id == "spk1" && firstNamed ? s with { Name = "Ana", Renamed = true } : s).ToList();
        var voices = new VoicesDocument(VoicesDocument.CurrentSchemaVersion, Model, "test", [], [
            new VoiceCluster("mic", 0, first, 20, ["s1"]),
            new VoiceCluster("mic", 1, second, 20, ["s2"]),
        ]);
        return (document.Segments, speakers, voices);
    }

    private static KnownVoice Known(string id, string name, float[] direction, string model = Model) => new()
    {
        Id = id,
        Name = name,
        EmbeddingModelId = model,
        Samples = [new KnownVoiceSample("earlier", "spk9", direction, 30, DateTimeOffset.UnixEpoch)],
        Recordings = ["earlier"],
    };

    [Fact]
    public void AVoiceAboveTheThresholdWithAClearMarginIsSuggested()
    {
        var (segments, speakers, voices) = Recording(At(0), At(90));

        // cos 30° = 0.866 for Sam; Lee is at 80° from spk1 (0.17) and 10° from spk2 (0.98).
        var found = VoiceMatcher.Match(segments, speakers, voices, [Known("v1", "Sam", At(30)), Known("v2", "Lee", At(80))], "rec", []);

        Assert.Equal([("spk1", "Sam"), ("spk2", "Lee")], found.OrderBy(f => f.SpeakerId).Select(f => (f.SpeakerId, f.Voice.Name)));
        Assert.InRange(found.Single(f => f.SpeakerId == "spk1").Similarity, 0.86, 0.87);
    }

    [Fact]
    public void BelowTheThresholdNothingIsSuggested()
    {
        var (segments, speakers, voices) = Recording(At(0), At(180));

        // cos 54° = 0.588, just under 0.60.
        Assert.Empty(VoiceMatcher.Match(segments, speakers, voices, [Known("v1", "Sam", At(54))], "rec", []));
    }

    [Fact]
    public void AnExpectedNameNeedsLess()
    {
        var (segments, speakers, voices) = Recording(At(0), At(180));
        var sam = Known("v1", "Sam Okafor", At(54));

        // 0.588 is under 0.60 but over 0.50 when Sam is one of the recording's participants or Who spoke names.
        var found = VoiceMatcher.Match(segments, speakers, voices, [sam], "rec", ["  sam okafor "]);

        var match = Assert.Single(found);
        Assert.True(match.Preferred);
        Assert.Equal("spk1", match.SpeakerId);
    }

    [Fact]
    public void TwoKnownVoicesTooAlikeGiveNoSuggestion()
    {
        var (segments, speakers, voices) = Recording(At(0), At(180));

        // cos 10° = 0.985 and cos 15° = 0.966: both match, 0.019 apart, under the 0.10 margin.
        Assert.Empty(VoiceMatcher.Match(segments, speakers, voices, [Known("v1", "Sam", At(10)), Known("v2", "Lee", At(15))], "rec", []));
    }

    [Fact]
    public void MutedDeclinedAndAlreadyNamedVoicesAreNotSuggestedButStillCountAsSecondBest()
    {
        var (segments, speakers, voices) = Recording(At(0), At(90), firstNamed: true);
        var muted = Known("v1", "Sam", At(100)) with { Suggest = false };
        var declined = Known("v2", "Lee", At(95)) with { DeclinedIn = ["rec"] };

        Assert.Empty(VoiceMatcher.Match(segments, speakers, voices, [muted], "rec", []));
        Assert.Empty(VoiceMatcher.Match(segments, speakers, voices, [declined], "rec", []));
        Assert.Single(VoiceMatcher.Match(segments, speakers, voices, [declined], "another", []));

        // Ana already names spk1 here, so her voice is not offered for spk2 even when it matches.
        Assert.Empty(VoiceMatcher.Match(segments, speakers, voices, [Known("v3", "ana", At(90))], "rec", []));

        // A muted voice more alike than the suggestable one blocks it (the best is not the one offered).
        Assert.Empty(VoiceMatcher.Match(segments, speakers, voices, [Known("v4", "Kim", At(110)), muted with { DeclinedIn = [] }], "rec", []));
    }

    [Fact]
    public void OneSpeakerPerVoiceTheMostAlikeWins()
    {
        // Both speakers sound like Sam; spk2 more so.
        var (segments, speakers, voices) = Recording(At(20), At(5));

        var found = VoiceMatcher.Match(segments, speakers, voices, [Known("v1", "Sam", At(0))], "rec", []);

        Assert.Equal("spk2", Assert.Single(found).SpeakerId);
    }

    [Fact]
    public void VoicesOfAnotherModelAreNeverCompared()
    {
        var (segments, speakers, voices) = Recording(At(0), At(90));

        Assert.Empty(VoiceMatcher.Match(segments, speakers, voices, [Known("v1", "Sam", At(0), model: "3dspeaker-eres2net-base")], "rec", []));
    }

    [Fact]
    public void ASpeakerWithTooLittleSpeechGetsNoSuggestion()
    {
        var document = Document(Segment("s1", 0, 6, "Short.", "spk1"), Segment("s2", 6, 40, "Long.", "spk2"));
        var voices = new VoicesDocument(VoicesDocument.CurrentSchemaVersion, Model, "test", [], [
            new VoiceCluster("mic", 0, At(0), 6, ["s1"]),
            new VoiceCluster("mic", 1, At(90), 34, ["s2"]),
        ]);

        var found = VoiceMatcher.Match(document.Segments, document.Speakers, voices, [Known("v1", "Sam", At(0)), Known("v2", "Lee", At(90))], "rec", []);

        Assert.Equal("spk2", Assert.Single(found).SpeakerId);
    }

    [Fact]
    public void WithoutVoicesNothingIsSuggested()
    {
        var (segments, speakers, _) = Recording(At(0), At(90));

        Assert.Empty(VoiceMatcher.Match(segments, speakers, null, [Known("v1", "Sam", At(0))], "rec", []));
    }

    [Fact]
    public void TheSignatureIsTheMeanOfTheLastTenConfirmations()
    {
        var samples = Enumerable.Range(0, 12)
            .Select(i => new KnownVoiceSample($"r{i}", "spk1", i < 2 ? At(90) : At(0), 30, DateTimeOffset.UnixEpoch.AddDays(i)))
            .ToList();
        var voice = Known("v1", "Sam", At(0)) with { Samples = samples };

        // The two oldest (at 90°) no longer count: the signature is along x.
        Assert.InRange(voice.Signature()!.Direction[0], 0.999, 1.001);

        var mixed = voice with { Samples = [samples[0], samples[5]] };
        Assert.InRange(mixed.Signature()!.Direction[0], 0.70, 0.71);
    }
}
