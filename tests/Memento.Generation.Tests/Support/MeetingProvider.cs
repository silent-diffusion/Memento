using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Memento.AI;
using Memento.Generation.Generation;

namespace Memento.Generation.Tests.Support;

/// <summary>
/// A deterministic stand-in for a model that has read the synthetic meeting, with the mistakes the spike saw small
/// models make. The map pass answers from the ground truth of the lines in each chunk, plus: a citation one line off
/// (D2, A3), recap duplicates (D1, A1), owners by first name, planted false claims (a wrong owner, a wrong date, a parked
/// price rise and weekend cover as decisions, payment initiation, a fabricated point), a quote that is not in the
/// transcript, and an undiscussed agenda item marked as discussed. Some wrong copies come before the right one, so the
/// reducer's alternates are exercised. The verifier is correct against the ground truth, except that it is fooled by the
/// two parked items (the validator must catch those).
/// </summary>
internal sealed partial class MeetingProvider(AiProviderKind kind = AiProviderKind.Local, int context = 16384) : IAiProvider
{
    public const string FabricatedPoint = "The team agreed to hire two engineers in the first quarter.";

    /// <summary>The planted false claims and the text that must not survive as stated.</summary>
    public static IReadOnlyList<string> PlantedDecisions { get; } =
    [
        "The team decided to raise the Pro price to 19 dollars.",
        "Release 3.2 will ship on November 19.",
        "The team decided to start weekend support coverage.",
        "The first version of the Tallyhouse integration will include payment initiation.",
        "The team agreed to hire two engineers.",
    ];

    public ConcurrentBag<AiRequest> Requests { get; } = [];

    public string Id => kind == AiProviderKind.Local ? "local" : "anthropic";

    public string DisplayName => kind == AiProviderKind.Local ? "Local model" : "Claude";

    public AiProviderKind Kind => kind;

    public string Model => kind == AiProviderKind.Local ? "qwen3-5-4b-q4" : "claude-opus-5-5";

    public AiCapabilities Capabilities { get; } = new(context, context / 2, true, kind == AiProviderKind.Local, true, false);

    public int CountTokens(string text) => EstimatingTokenCounter.Generic.Count(text);

    public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(AiReadiness.Ready());

    public Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        var user = request.Messages[^1].Content;
        JsonNode answer = request.Purpose switch
        {
            var p when p.StartsWith("map.commitments", StringComparison.Ordinal) => Commitments(ChunkLines(user)),
            var p when p.StartsWith("map.agenda", StringComparison.Ordinal) => AgendaCoverage(ChunkLines(user)),
            var p when p.StartsWith("map.next", StringComparison.Ordinal) => new JsonObject { ["items"] = new JsonArray() },
            var p when p.StartsWith("map.quotes", StringComparison.Ordinal) => new JsonObject { ["quotes"] = new JsonArray() },
            var p when p.StartsWith("map.", StringComparison.Ordinal) => Points(request.Purpose, ChunkLines(user)),
            "verify.batch" => Batch(user),
            _ => Verdict(ExcerptOf(user), ClaimOf(user)),
        };
        var text = answer.ToJsonString();
        using var document = JsonDocument.Parse(text);
        return Task.FromResult(new AiResponse(Id, Model, text, document.RootElement.Clone(), AiStopReason.Completed, "eog", new AiUsage(CountTokens(user), CountTokens(text)), new AiTimings(TimeSpan.FromMilliseconds(5)), AiRequestHash.Compute(request, Id, Model)));
    }

    private static List<int> ChunkLines(string user) => LineMarker().Matches(user).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToList();

    private static string Words(int line, int count = 10) =>
        string.Join(' ', SyntheticMeeting.Lines[line - 1].Text.Split(' ').Take(count)).TrimEnd(',', '.');

    private static JsonObject Citation(int line, string quote) => new() { ["line"] = line, ["quote"] = quote };

    private static readonly Dictionary<string, (string? Owner, string? Due)> Spoken = new(StringComparer.Ordinal)
    {
        ["A1"] = ("Luis", "by Friday"),
        ["A2"] = ("Mei", "by next Wednesday"),
        ["A3"] = ("Sam", "by tomorrow"),
        ["A4"] = ("Luis", "by Monday"),
        ["A5"] = ("Dana", "this week"),
        ["A6"] = ("Mei", null),
    };

    private static readonly int WorksForMe = SyntheticMeeting.LinesOf("D1")[1];
    private static readonly int ReadOnlyFirst = SyntheticMeeting.LinesOf("D5")[0];
    private static readonly int Mockups = SyntheticMeeting.LinesOf("A2")[0];
    private static readonly int HelpCenter = SyntheticMeeting.LinesOf("U1")[0];
    private static readonly int Nineteen = ByText("Nineteen sounds right");
    private static readonly int Park = ByText("Actually, wait.");
    private static readonly int WeekendThen = ByText("So we're doing weekend coverage then?");
    private static readonly int Revisit = ByText("Let me be careful here.");
    private static readonly int AutoReply = ByText("Could we at least add an auto-reply");
    private static readonly int ResponseTime = ByText("Right. First response time");
    private static readonly int OpenQuestion = ByText("Agreed. I'll need to write it up properly");

    private static int ByText(string start) => SyntheticMeeting.Lines.Single(l => l.Text.StartsWith(start, StringComparison.Ordinal)).ShortId;

    /// <summary>The line where each action item is committed to (the speaker who takes it on), when it is not the first tagged one.</summary>
    private static readonly Dictionary<string, int> CommitmentLine = new(StringComparer.Ordinal)
    {
        ["A1"] = SyntheticMeeting.LinesOf("A1")[^1], ["A4"] = SyntheticMeeting.LinesOf("A4")[^1],
    };

    private static JsonObject Commitments(List<int> lines)
    {
        var decisions = new JsonArray();
        var actions = new JsonArray();
        void Decision(string text, int line, string? quote = null) => decisions.Add(new JsonObject { ["decision"] = text, ["citation"] = Citation(line, quote ?? Words(line)) });
        void Action(string text, string? owner, string? due, int line, string? quote = null) =>
            actions.Add(new JsonObject { ["task"] = text, ["owner"] = owner, ["due"] = due, ["citation"] = Citation(line, quote ?? Words(line)) });

        // Wrong copies that come first: the alternates must rescue the true claims.
        if (lines.Contains(WorksForMe))
        {
            Decision(PlantedDecisions[1], WorksForMe);
        }

        if (lines.Contains(ReadOnlyFirst))
        {
            Decision(PlantedDecisions[3], ReadOnlyFirst);
        }

        foreach (var truth in SyntheticMeeting.Truths.Where(t => t.Kind is "decision" or "action"))
        {
            var quoted = CommitmentLine.TryGetValue(truth.Id, out var line) ? line : SyntheticMeeting.LinesOf(truth.Id)[0];
            if (!lines.Contains(quoted))
            {
                continue;
            }

            // The quote is right, the line number is not: citation repair moves these back.
            var cited = truth.Id switch
            {
                "D2" => quoted - 1,
                "A3" => quoted + 1,
                _ => quoted,
            };

            if (truth.Kind == "decision")
            {
                Decision(truth.Text, cited, Words(quoted));
            }
            else
            {
                var (owner, due) = Spoken.GetValueOrDefault(truth.Id);
                Action(truth.Text, owner, due, cited, Words(quoted));
            }
        }

        // Recap duplicates.
        foreach (var recap in SyntheticMeeting.RecapLines.Where(lines.Contains))
        {
            if (SyntheticMeeting.Lines[recap - 1].Tags.Contains("D1"))
            {
                Decision("3.2 ships on November twelfth.", recap, "3.2 ships November twelfth");
            }

            if (SyntheticMeeting.Lines[recap - 1].Tags.Contains("A1"))
            {
                Action("Cut the 3.3 branch and flag the template code.", "Luis", "by Friday", recap, "Luis cuts the 3.3 branch and flags the template code by Friday");
            }
        }

        // Planted false claims.
        if (lines.Contains(Mockups))
        {
            Action("Deliver the final pricing page mockups.", "Luis", "next Wednesday", Mockups);
        }

        if (lines.Contains(HelpCenter))
        {
            Action("Update the help-center article on offline mode.", "Sam", "Friday", HelpCenter);
        }

        if (lines.Contains(Nineteen))
        {
            Decision(PlantedDecisions[0], Nineteen);
        }

        if (lines.Contains(WeekendThen))
        {
            Decision(PlantedDecisions[2], WeekendThen);
        }

        if (lines.Contains(AutoReply))
        {
            Decision(PlantedDecisions[4], AutoReply, "we will hire two engineers in January");
        }

        // A weak model takes parked items for decisions (the verifier below is fooled too; the validator is not).
        if (lines.Contains(Park))
        {
            Decision("The Pro price change is parked until the pricing review in late November.", Park, "We park the price change until the pricing review");
        }

        if (lines.Contains(Revisit))
        {
            Decision("Weekend support coverage will be revisited on the thirtieth.", Revisit, "revisit this on the thirtieth");
        }

        return new JsonObject { ["decisions"] = decisions, ["action_items"] = actions };
    }

    private static readonly IReadOnlyDictionary<int, string[]> AgendaWords = new Dictionary<int, string[]>
    {
        [1] = ["3.2 release", "3.2 board"],
        [2] = ["offline mode backlog", "offline"],
        [3] = ["pricing page"],
        [4] = ["support trends"],
        [5] = ["accessibility"],
        [6] = ["tallyhouse"],
        [7] = ["hiring"],
    };

    private static JsonObject AgendaCoverage(List<int> lines)
    {
        var items = new JsonArray();
        foreach (var (id, words) in AgendaWords)
        {
            var line = lines.FirstOrDefault(l => words.Any(w => SyntheticMeeting.Lines[l - 1].Text.Contains(w, StringComparison.OrdinalIgnoreCase)));
            if (id == 7 && lines.Contains(ResponseTime))
            {
                // A false positive: weekend response times are not the hiring plan.
                items.Add(new JsonObject { ["id"] = id, ["discussed"] = true, ["line"] = ResponseTime, ["quote"] = "First response time on weekend tickets" });
                continue;
            }

            items.Add(line == 0
                ? new JsonObject { ["id"] = id, ["discussed"] = false, ["line"] = null, ["quote"] = null }
                : new JsonObject { ["id"] = id, ["discussed"] = true, ["line"] = line, ["quote"] = Words(line, 6) });
        }

        return new JsonObject { ["items"] = items };
    }

    private static JsonObject Points(string purpose, List<int> lines)
    {
        var points = new JsonArray();
        foreach (var truth in SyntheticMeeting.Truths.Where(t => t.Kind == "decision"))
        {
            var line = SyntheticMeeting.LinesOf(truth.Id)[0];
            if (lines.Contains(line))
            {
                points.Add(new JsonObject { ["text"] = truth.Text, ["line"] = line, ["quote"] = Words(line) });
            }
        }

        if (purpose.StartsWith("map.openQuestions", StringComparison.Ordinal) && lines.Contains(OpenQuestion))
        {
            points.Add(new JsonObject { ["text"] = "How long the conflict log keeps old versions is still open.", ["line"] = OpenQuestion, ["quote"] = "There are open questions about how long we keep the old versions" });
        }

        if (lines.Contains(AutoReply))
        {
            points.Add(new JsonObject { ["text"] = FabricatedPoint, ["line"] = AutoReply, ["quote"] = Words(AutoReply) });
        }

        return new JsonObject { ["points"] = points };
    }

    private static JsonObject Batch(string user)
    {
        var verdicts = new JsonArray();
        foreach (Match item in BatchItem().Matches(user))
        {
            var verdict = Verdict(ExcerptOf(item.Groups[2].Value), ClaimOf(item.Groups[2].Value));
            verdict["item"] = int.Parse(item.Groups[1].Value, CultureInfo.InvariantCulture);
            verdicts.Add(verdict);
        }

        return new JsonObject { ["verdicts"] = verdicts };
    }

    private static string ExcerptOf(string text) => text.Split("\n\nClaim:")[0];

    private static string ClaimOf(string text) => text.Split("Claim: ").Last().Trim();

    /// <summary>A verifier that knows the ground truth of the excerpt's lines.</summary>
    private static JsonObject Verdict(string excerpt, string claim)
    {
        var lines = ChunkLines(excerpt);
        var tags = lines.SelectMany(l => SyntheticMeeting.Lines[l - 1].Tags).ToHashSet(StringComparer.Ordinal);
        var truths = SyntheticMeeting.Truths.Where(t => tags.Contains(t.Id)).ToList();
        bool supported;
        if (claim.StartsWith("Decision: ", StringComparison.Ordinal))
        {
            var text = claim["Decision: ".Length..];
            // Fooled by parked items, as small models were in the spike.
            supported = truths.Any(t => t.Kind == "decision" && t.Matches(text)) || truths.Any(t => t.Kind == "deferred" && t.Matches(text) && !PlantedDecisions.Contains(text));
        }
        else if (claim.StartsWith("Action item: ", StringComparison.Ordinal))
        {
            supported = truths.Any(t => t.Kind == "action" && t.Matches(claim["Action item: ".Length..]));
        }
        else if (OwnerClaim().Match(claim) is { Success: true } owner)
        {
            supported = truths.Any(t => t.Kind == "action" && t.Matches(owner.Groups[2].Value) && t.Owner is { } o && owner.Groups[1].Value.Contains(o, StringComparison.OrdinalIgnoreCase));
        }
        else if (DueClaim().Match(claim) is { Success: true } due)
        {
            supported = truths.Any(t => t.Kind == "action" && t.Matches(due.Groups[2].Value) && t.DueKey is { } d && due.Groups[1].Value.Contains(d, StringComparison.OrdinalIgnoreCase));
        }
        else if (AgendaClaim().Match(claim) is { Success: true } agenda)
        {
            var index = SyntheticMeeting.Agenda.ToList().IndexOf(agenda.Groups[1].Value) + 1;
            supported = index > 0 && !SyntheticMeeting.AgendaNotDiscussed.Contains(index);
        }
        else
        {
            var words = TextMatch.Words(claim);
            var have = TextMatch.Words(excerpt);
            supported = claim != FabricatedPoint && words.Count > 0 && words.Count(have.Contains) * 2 >= words.Count;
            supported |= truths.Any(t => t.Kind == "decision" && t.Matches(claim));
        }

        return new JsonObject { ["reason"] = supported ? "The excerpt states it." : "The excerpt does not say this.", ["supported"] = supported };
    }

    [GeneratedRegex(@"^\[(\d+)\] ", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LineMarker();

    [GeneratedRegex(@"<item number=""(\d+)"">\n(.*?)\n</item>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BatchItem();

    [GeneratedRegex(@"^(.+) is the person who will do this task: (.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerClaim();

    [GeneratedRegex(@"^The deadline stated for this task is ""(.+)"": (.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DueClaim();

    [GeneratedRegex(@"^This excerpt discusses the agenda item ""(.+)""\.$", RegexOptions.CultureInvariant)]
    private static partial Regex AgendaClaim();
}
