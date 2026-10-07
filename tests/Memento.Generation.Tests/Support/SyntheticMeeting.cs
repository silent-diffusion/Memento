using System.Globalization;
using Memento.AI.Payload;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Transcripts;
using Memento.Generation.Documents;

namespace Memento.Generation.Tests.Support;

/// <summary>
/// The October 2026 spike's synthetic 20-minute product meeting (fictional company and people; fixtures/meeting_source.txt,
/// one line per segment: speaker|text|tags) with its ground truth: five decisions, six owned action items, three action
/// items without owner or date, and two items explicitly parked (not decisions). Two agenda items are never discussed.
/// Segment times are spread over 1,200 s by word count, as the spike did.
/// </summary>
public static class SyntheticMeeting
{
    public const string RecordingId = "20261005-160000-abcdef";

    public static IReadOnlyList<string> Agenda { get; } =
    [
        "Release 3.2 scope and date",
        "Offline mode bug backlog",
        "Pricing page redesign",
        "Support ticket trends",
        "Accessibility audit results",
        "Tallyhouse bank-feed integration",
        "Q1 hiring plan",
    ];

    /// <summary>1-based agenda items the meeting never reaches.</summary>
    public static IReadOnlyList<int> AgendaNotDiscussed { get; } = [5, 7];

    public static IReadOnlyList<(string Id, string Name)> Speakers { get; } =
    [
        ("spk1", "Dana Okafor"),
        ("spk2", "Luis Brandt"),
        ("spk3", "Mei Tanaka"),
        ("spk4", "Sam Whitfield"),
    ];

    /// <summary>The ground truth, with the spike's keyword groups (every group must match the claim's text or quote).</summary>
    public static IReadOnlyList<Truth> Truths { get; } =
    [
        new("D1", "decision", "Release 3.2 ships on Thursday, November 12.", null, null, [["november 12", "november twelfth", "12 november", "nov 12", "november 12th"]]),
        new("D2", "decision", "Recurring invoice templates move out of 3.2 into 3.3.", null, null, [["recurring", "template"], ["3 3", "out of 3 2", "move", "defer", "postpone", "remov", "cut", "pull", "exclud", "drop"]]),
        new("D3", "decision", "Sync conflicts: last write wins plus a conflict log for 3.2.", null, null, [["last write", "conflict log"]]),
        new("D4", "decision", "Remove the annual-only Business plan; monthly and annual toggle with monthly as the default.", null, null, [["annual"], ["toggle", "remov", "drop", "monthly"]]),
        new("D5", "decision", "Tallyhouse: read-only bank feed first, no payment initiation in the first version.", null, null, [["read only", "bank feed"], ["tallyhouse", "bank feed", "payment"]]),
        new("A1", "action", "Cut the 3.3 branch and put the recurring template code behind a feature flag.", "luis", "friday", [["branch", "feature flag", "flag"]]),
        new("A2", "action", "Deliver the final pricing page mockups (version B).", "mei", "wednesday", [["mockup"]]),
        new("A3", "action", "Send the top twenty offline sync tickets to Luis.", "sam", "tomorrow", [["ticket"], ["top", "twenty", "20", "list"]]),
        new("A4", "action", "Write the conflict log design doc.", "luis", "monday", [["design doc", "design document", "document", "write"], ["conflict"]]),
        new("A5", "action", "Email Tallyhouse to confirm the read-only scope and ask for sandbox credentials.", "dana", "week", [["tallyhouse", "sandbox"], ["email", "contact", "confirm", "credential"]]),
        new("A6", "action", "Run five usability sessions on the pricing toggle.", "mei", null, [["usability", "session"]]),
        new("U1", "action", "Update the help-center article on offline mode.", null, null, [["help center", "article"]]),
        new("U2", "action", "Tell the sales team about the annual plan change before the page goes live.", null, null, [["sales"]]),
        new("U3", "action", "The release notes must explain the new sync conflict behaviour.", null, null, [["release note"]]),
        new("F1", "deferred", "The Pro price increase to 19 dollars is parked until the pricing review.", null, null, [["19", "nineteen", "pro price", "price increase", "price change", "pricing review"]]),
        new("F2", "deferred", "Weekend support coverage is not decided; it comes back on the 30th.", null, null, [["weekend"]]),
    ];

    /// <summary>The meeting's lines: speaker, text and tags, in order (short id = position + 1).</summary>
    public static IReadOnlyList<SourceLine> Lines { get; } = Load();

    /// <summary>Short ids of the lines tagged with a truth id, recap lines (tag R) left out unless asked for.</summary>
    public static IReadOnlyList<int> LinesOf(string truthId, bool withRecap = false) =>
        Lines.Where(l => l.Tags.Contains(truthId) && (withRecap || !l.Tags.Contains("R"))).Select(l => l.ShortId).ToList();

    public static IReadOnlyList<int> RecapLines => Lines.Where(l => l.Tags.Contains("R")).Select(l => l.ShortId).ToList();

    public static TranscriptDocument Transcript()
    {
        var segments = Lines.Select(l => new TranscriptSegment(
            string.Create(CultureInfo.InvariantCulture, $"s{l.ShortId:0000}"),
            l.Start,
            l.End,
            "system",
            Speakers.First(s => s.Name == l.Speaker).Id,
            0.95,
            l.Text,
            0.9,
            [],
            null)).ToList();
        var speakers = Speakers.Select((s, i) => new Speaker(s.Id, s.Name, Renamed: true, (i % 4) + 1, (long)(segments.Where(g => g.Speaker == s.Id).Sum(g => g.End - g.Start) * 1000))).ToList();
        return new TranscriptDocument
        {
            Language = "en",
            Engine = new TranscriptEngineInfo("whisper.cpp", "large-v3-turbo", "GPU (Vulkan)", "1.9", 1000),
            Speakers = speakers,
            Segments = segments,
            Version = 1,
        };
    }

    public static ProjectManifest Manifest() => new()
    {
        Id = RecordingId,
        CreatedAt = new DateTimeOffset(2026, 10, 5, 16, 0, 0, TimeSpan.FromHours(1)),
        ModifiedAt = new DateTimeOffset(2026, 10, 5, 16, 25, 0, TimeSpan.FromHours(1)),
        State = ProjectStates.Ready,
        DurationMs = 1_200_000,
        Details = new ProjectDetails
        {
            Title = "Ledgerly weekly product sync",
            Type = "meeting",
            Participants = Speakers.Select(s => s.Name).ToList(),
            Platform = "Zoom",
            Organization = "Fernhollow Software",
            Agenda = new Agenda("agenda.txt", true, Agenda.Select((a, i) => new AgendaItem("a" + (i + 1).ToString(CultureInfo.InvariantCulture), a, false, false, null)).ToList()),
        },
    };

    public static RecordingMaterial Material() =>
        new(RecordingId, Manifest(), Transcript(), new AnnotationsDocument(), [], []);

    public static PayloadSelection AllInputs { get; } = new()
    {
        Transcript = true,
        Details = true,
        Participants = true,
        Agenda = true,
        ChaptersAndTopics = true,
        Highlights = true,
        Instructions = true,
    };

    private static List<SourceLine> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "meeting_source.txt");
        var parts = File.ReadAllLines(path).Where(l => !l.StartsWith('#') && l.Contains('|', StringComparison.Ordinal)).Select(l => l.Split('|')).ToList();
        var words = parts.Sum(p => p[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        const double total = 1200, gap = 0.9;
        var rate = (total - (gap * parts.Count)) / words;
        var lines = new List<SourceLine>();
        var t = 1.2;
        for (var i = 0; i < parts.Count; i++)
        {
            var w = parts[i][1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var d = Math.Max(1.0, w * rate);
            lines.Add(new SourceLine(i + 1, Math.Round(t, 1), Math.Round(t + d, 1), parts[i][0], parts[i][1], parts[i].Length > 2 ? parts[i][2].Split(' ', StringSplitOptions.RemoveEmptyEntries) : []));
            t += d + gap;
        }

        return lines;
    }

    public sealed record SourceLine(int ShortId, double Start, double End, string Speaker, string Text, IReadOnlyList<string> Tags);

    /// <param name="Owner">The owner's first name in lower case, or <c>null</c> when none was named.</param>
    /// <param name="DueKey">A word the stated deadline contains, or <c>null</c> when none was stated.</param>
    public sealed record Truth(string Id, string Kind, string Text, string? Owner, string? DueKey, IReadOnlyList<IReadOnlyList<string>> Keywords)
    {
        /// <summary>Every keyword group has a match in the normalised text.</summary>
        public bool Matches(string text)
        {
            var hay = " " + Generation.TextMatch.Normalize(text) + " ";
            return Keywords.All(group => group.Any(k => hay.Contains(Generation.TextMatch.Normalize(k), StringComparison.Ordinal)));
        }
    }
}
