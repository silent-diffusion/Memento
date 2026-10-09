using Memento.Core.Bridge.Contracts;
using Memento.Transcription.Speakers;

namespace Memento.Transcription.Tests;

public sealed class SpeakerNamerTests
{
    private static readonly string[] NoNames = [];

    private static TranscriptSegment Line(string id, double start, double end, string speaker, string track = "mic") =>
        new(id, start, end, track, speaker, 0.9, "text", 0.9, [], null);

    private static Speaker Unnamed(int n) => new($"spk{n}", $"Speaker {n}", false, ((n - 1) % 4) + 1, 0);

    /// <summary>The new pass: spk1 speaks first (0–10, 20–30), then spk2 (10–20), then spk3 (30–35).</summary>
    private static (Speaker[] Speakers, TranscriptSegment[] Segments) NewPass() =>
        ([Unnamed(1), Unnamed(2), Unnamed(3)],
        [Line("s1", 0, 10, "spk1"), Line("s2", 10, 20, "spk2"), Line("s3", 20, 30, "spk1"), Line("s4", 30, 35, "spk3")]);

    [Fact]
    public void KnownNamesGoToTheSpeakersInOrderOfFirstAppearance()
    {
        var (speakers, segments) = NewPass();

        var result = SpeakerNamer.Name(speakers, segments, null, null, ["Avery Stone", "Rowan Hale"]);

        Assert.Equal(["Avery Stone", "Rowan Hale", "Speaker 3"], result.Speakers.Select(s => s.Name));
        Assert.Equal([true, true, false], result.Speakers.Select(s => s.Renamed));
        Assert.Equal((0, 2), (result.Carried, result.Known));
        Assert.Equal(["spk1", "spk2", "spk3"], result.Speakers.Select(s => s.Id));
    }

    [Fact]
    public void ANameGivenBeforeGoesToTheSpeakerThatTookItsLines()
    {
        var (speakers, segments) = NewPass();
        // Before, the user named the speaker of 10–20 and 30–35 "Sam Okafor"; the new pass hears 10–20 as spk2.
        Speaker[] before = [new("spk1", "Speaker 1", false, 1, 0), new("spk7", "Sam Okafor", true, 3, 0)];
        TranscriptSegment[] old = [Line("s1", 0, 10, "spk1"), Line("s2", 10, 20, "spk7"), Line("s3", 20, 30, "spk1"), Line("s4", 30, 35, "spk7")];

        var result = SpeakerNamer.Name(speakers, segments, before, old, ["Sam Okafor", "Avery Stone"]);

        // Sam keeps spk2 (10 s shared); Sam is not given twice, so Avery goes to the first unnamed speaker.
        Assert.Equal(["Avery Stone", "Sam Okafor", "Speaker 3"], result.Speakers.Select(s => s.Name));
        Assert.Equal((1, 1), (result.Carried, result.Known));
    }

    [Fact]
    public void ANameOnlyGrazedByTheNewSpeakerIsNotCarried()
    {
        var (speakers, segments) = NewPass();
        Speaker[] before = [new("spk9", "Rowan Hale", true, 1, 0)];
        // Rowan said 34–60 before; the new spk3 overlaps only 1 s of that (less than a quarter of either's talk time).
        TranscriptSegment[] old = [Line("o1", 34, 60, "spk9")];

        var result = SpeakerNamer.Name(speakers, segments, before, old, NoNames);

        Assert.DoesNotContain(result.Speakers, s => s.Name == "Rowan Hale");
        Assert.Equal(0, result.Carried);
    }

    [Fact]
    public void NamesFromAnotherTrackAreNotCarried()
    {
        var (speakers, segments) = NewPass();
        Speaker[] before = [new("spk5", "Rowan Hale", true, 1, 0)];
        TranscriptSegment[] old = [Line("o1", 0, 10, "spk5", track: "system")];

        var result = SpeakerNamer.Name(speakers, segments, before, old, NoNames);

        Assert.All(result.Speakers, s => Assert.False(s.Renamed));
    }

    [Fact]
    public void MoreNamesThanSpeakersLeaveTheRestUnused()
    {
        var (speakers, segments) = NewPass();

        var result = SpeakerNamer.Name(speakers, segments, null, null, ["A", "B", "C", "D", "E"]);

        Assert.Equal(["A", "B", "C"], result.Speakers.Select(s => s.Name));
        Assert.Equal(3, result.Known);
    }
}
