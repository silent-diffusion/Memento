using System.Globalization;
using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><see cref="ChapterSuggester"/> and <c>annotations.suggestChapters</c> / <c>dismissSuggestion</c> / <c>restoreSuggestion</c>.</summary>
public sealed class ChapterSuggesterTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static readonly string[][] Subjects =
    [
        ["budget", "invoices", "spending", "forecast", "accounts"],
        ["hiring", "candidates", "interviews", "recruiter", "onboarding"],
        ["website", "homepage", "navigation", "design", "colours"],
    ];

    /// <summary>
    /// Three subjects of six minutes each, a line every 10 s by alternating speakers; each subject change comes after a
    /// 4 s pause, and the line after it is by a new speaker.
    /// </summary>
    private static TranscriptSegment[] ThreeSubjects()
    {
        var lines = new List<TranscriptSegment>();
        var t = 0.0;
        for (var subject = 0; subject < Subjects.Length; subject++)
        {
            if (subject > 0)
            {
                t += 4;
            }

            for (var i = 0; i < 36; i++)
            {
                var words = Subjects[subject];
                var text = string.Create(CultureInfo.InvariantCulture, $"We talked about the {words[i % 5]} and the {words[(i + 2) % 5]} again, then the {words[(i + 1) % 5]}.");
                var speaker = subject == 0 ? (i % 2 == 0 ? "spk1" : "spk2") : subject == 1 ? (i % 2 == 0 ? "spk3" : "spk1") : (i % 2 == 0 ? "spk2" : "spk3");
                lines.Add(Segment(string.Create(CultureInfo.InvariantCulture, $"s{lines.Count + 1:0000}"), t, t + 8, text, speaker));
                t += 10;
            }
        }

        return [.. lines];
    }

    [Fact]
    public void SubjectChangesBecomeChaptersAtTheLineAfterThePause()
    {
        var suggestions = ChapterSuggester.Suggest(ThreeSubjects(), []);

        Assert.Equal([0L, 364_000L, 728_000L], suggestions.Select(s => s.AtMs));
        Assert.Equal("Start of the recording", suggestions[0].Basis);
        Assert.Equal("The subject changes · 6 s pause · new speaker", suggestions[1].Basis);
        Assert.All(suggestions, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
        Assert.Equal(3, suggestions.Select(s => s.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(suggestions[1].Title.ToLowerInvariant(), Subjects[1]);
    }

    [Fact]
    public void ATopicNamesTheChapterThatIsAboutIt()
    {
        var suggestions = ChapterSuggester.Suggest(ThreeSubjects(), ["Hiring candidates"]);

        Assert.Equal("Hiring candidates", suggestions[1].Title);
    }

    [Fact]
    public void TheSameTranscriptAlwaysGivesTheSameSuggestions()
    {
        var first = ChapterSuggester.Suggest(ThreeSubjects(), ["Budget"]);
        var second = ChapterSuggester.Suggest(ThreeSubjects().Reverse().ToArray(), ["Budget"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AShortOrUniformRecordingGetsNone()
    {
        Assert.Empty(ChapterSuggester.Suggest(ThreeSubjects().Take(30).ToList(), []));

        var uniform = Enumerable.Range(0, 80)
            .Select(i => Segment(string.Create(CultureInfo.InvariantCulture, $"u{i}"), i * 10, (i * 10) + 8, "The budget and the invoices and the forecast again.", "spk1"))
            .ToList();
        Assert.Empty(ChapterSuggester.Suggest(uniform, []));
    }

    private async Task<string> RecordingAsync()
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => Document(ThreeSubjects()), CancellationToken.None);
        return id;
    }

    private Task<JsonElement> ResultAsync(string method, object parameters) => _host.ResultAsync(method, JsonSerializer.Serialize(parameters));

    private static List<long> Times(JsonElement result) => result.GetProperty("suggestions").EnumerateArray().Select(s => s.GetProperty("atMs").GetInt64()).ToList();

    [Fact]
    public async Task SuggestionsLeaveOutChaptersAndDismissedOnesUntilRestored()
    {
        var id = await RecordingAsync();

        var all = await ResultAsync("annotations.suggestChapters", new { recordingId = id });
        Assert.Equal([0L, 364_000L, 728_000L], Times(all));
        Assert.Equal("sc364000", all.GetProperty("suggestions")[1].GetProperty("id").GetString());

        // Accepting is an ordinary chapter; one within a minute hides the suggestion.
        await ResultAsync("annotations.addChapter", new { recordingId = id, chapter = new { atMs = 370_000, title = "Hiring", origin = "local" } });
        Assert.Equal([0L, 728_000L], Times(await ResultAsync("annotations.suggestChapters", new { recordingId = id })));

        var dismissed = await ResultAsync("annotations.dismissSuggestion", new { recordingId = id, atMs = 728_000 });
        Assert.Equal([0L], Times(dismissed));
        var restored = await ResultAsync("annotations.restoreSuggestion", new { recordingId = id, atMs = 728_000 });
        Assert.Equal([0L, 728_000L], Times(restored));
    }

    [Fact]
    public async Task NoTranscriptNoSuggestionsAndBadParametersAreRefused()
    {
        var id = await _host.RecordAsync("Silent", 1, Mic);

        Assert.Empty((await ResultAsync("annotations.suggestChapters", new { recordingId = id })).GetProperty("suggestions").EnumerateArray());
        var negative = await _host.CallAsync("annotations.dismissSuggestion", JsonSerializer.Serialize(new { recordingId = id, atMs = -1 }));
        Assert.Equal("bridge.invalidParams", negative.GetProperty("error").GetProperty("code").GetString());
        var missing = await _host.CallAsync("annotations.dismissSuggestion", JsonSerializer.Serialize(new { recordingId = id }));
        Assert.Equal("bridge.invalidParams", missing.GetProperty("error").GetProperty("code").GetString());
        var unknown = await _host.CallAsync("annotations.suggestChapters", JsonSerializer.Serialize(new { recordingId = "20260101-000000-aaaaaa" }));
        Assert.Equal("project.notFound", unknown.GetProperty("error").GetProperty("code").GetString());
    }
}
