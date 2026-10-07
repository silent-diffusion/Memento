using System.Diagnostics;
using System.Globalization;
using System.Text;
using Memento.AI;
using Memento.AI.Local;
using Memento.AI.Payload;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Hardware;

/// <summary>
/// The spike's fixed verification set (14 true claims, 6 planted false ones), each checked against the lines of the
/// truth item it is about plus <c>pad</c> neighbours either side, asked with the pipeline's own verifier prompts:
/// one question per request, or numbered batches in claim order.
/// </summary>
internal static class FixedVerification
{
    public static readonly (string Claim, string Of, bool Expected)[] Claims =
    [
        ("Decision: Release 3.2 will ship on Thursday, November 12.", "D1", true),
        ("Decision: Recurring invoice templates are moved out of 3.2 into 3.3.", "D2", true),
        ("Decision: Sync conflicts will be handled with last write wins plus a conflict log.", "D3", true),
        ("Decision: The annual-only Business plan is removed from the pricing page and a monthly/annual toggle with monthly as default is shown.", "D4", true),
        ("Decision: The Tallyhouse integration starts with the read-only bank feed, without payment initiation.", "D5", true),
        ("Action item: Cut the 3.3 branch and put the recurring template code behind a feature flag.", "A1", true),
        ("Luis Brandt is the person who will do this task: Cut the 3.3 branch and put the recurring template code behind a feature flag.", "A1", true),
        ("Action item: Deliver final pricing page mockups.", "A2", true),
        ("Mei Tanaka is the person who will do this task: Deliver final pricing page mockups.", "A2", true),
        ("Action item: Send the list of the top twenty offline sync tickets to Luis.", "A3", true),
        ("The deadline stated for this task is \"by Monday\": Write the design doc for the conflict log.", "A4", true),
        ("Action item: Email the Tallyhouse contact to confirm the read-only scope and ask for sandbox credentials.", "A5", true),
        ("Action item: Run five usability sessions on the pricing toggle with existing customers.", "A6", true),
        ("Action item: Update the help-center article on offline mode.", "U1", true),
        ("Luis Brandt is the person who will do this task: Deliver the final pricing page mockups.", "A2", false),
        ("Decision: The team decided to raise the Pro price to 19 dollars.", "F1", false),
        ("Decision: Release 3.2 will ship on November 19.", "D1", false),
        ("Decision: The team decided to start weekend support coverage.", "F2", false),
        ("Sam Whitfield is the person who will do this task: Update the help-center article on offline mode.", "U1", false),
        ("Decision: The first version of the Tallyhouse integration will include payment initiation.", "D5", false),
    ];

    public static string Excerpt(TranscriptIndex transcript, string truthId, int pad)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var lines = SyntheticMeeting.LinesOf(truthId);
        var excerpt = new StringBuilder();
        foreach (var id in transcript.Ids.Where(id => id >= lines[0] - pad && id <= lines[^1] + pad).Order())
        {
            var entry = transcript.Find(id)!;
            excerpt.Append(CultureInfo.InvariantCulture, $"[{id}] {entry.Speaker}: {entry.Text}\n");
        }

        return excerpt.ToString().TrimEnd();
    }

    public static async Task<Result> RunAsync(LocalAiProvider provider, ComposedPayload payload, int pad, int batch, Func<AiRequest, AiRequest>? change = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(payload);
        var transcript = new TranscriptIndex(payload.TranscriptLines);
        var items = Claims.Select(c => (c.Claim, Excerpt: Excerpt(transcript, c.Of, pad))).ToList();
        var groups = batch <= 1 ? items.Select(i => new[] { i }).ToList() : items.Chunk(batch).ToList();
        var requests = groups.Select(g => batch <= 1 ? VerifyPrompts.ForStatement("verify.claim", g[0].Claim, g[0].Excerpt) : VerifyPrompts.BatchOf(g.ToList(), bounded: true)).Select(r => change is null ? r : change(r)).ToList();
        var clock = Stopwatch.StartNew();
        var responses = await provider.GenerateManyAsync(requests, null, CancellationToken.None);
        var seconds = clock.Elapsed.TotalSeconds;
        var answers = new VerifyAnswer?[Claims.Length];
        var next = 0;
        for (var r = 0; r < groups.Count; r++)
        {
            if (responses[r].Json is { } json)
            {
                if (batch <= 1)
                {
                    answers[next] = VerifyPrompts.ParseSingle(json);
                }
                else
                {
                    foreach (var (item, answer) in VerifyPrompts.ParseBatch(json))
                    {
                        if (item >= 1 && item <= groups[r].Length)
                        {
                            answers[next + item - 1] = answer;
                        }
                    }
                }
            }

            next += groups[r].Length;
        }

        // The pipeline's second vote: every fixed claim is a decision, an action item, an owner or a date, so "partly" is
        // asked again as yes/no over one more line either side.
        var borderline = Enumerable.Range(0, Claims.Length).Where(i => answers[i] is { Grade: VerifyAnswer.Partly }).ToList();
        if (borderline.Count > 0)
        {
            var second = await provider.GenerateManyAsync(borderline.Select(i => VerifyPrompts.SecondVote(Claims[i].Claim, Excerpt(transcript, Claims[i].Of, pad + 1))).ToList(), null, CancellationToken.None);
            for (var k = 0; k < borderline.Count; k++)
            {
                var vote = second[k].Json is { } json ? VerifyPrompts.ParseSingle(json) : null;
                answers[borderline[k]] = vote is { IsSupported: true } ? vote : answers[borderline[k]]! with { Grade = VerifyAnswer.NotSupported };
            }
        }

        seconds = clock.Elapsed.TotalSeconds;
        int right = 0, planted = 0, accepted = 0;
        var wrong = new List<string>();
        for (var i = 0; i < Claims.Length; i++)
        {
            if (answers[i]?.IsSupported == Claims[i].Expected)
            {
                right++;
                planted += Claims[i].Expected ? 0 : 1;
                accepted += Claims[i].Expected ? 1 : 0;
            }
            else
            {
                wrong.Add($"{Claims[i].Claim} | {answers[i]?.Grade ?? "no answer"}: {answers[i]?.Reason}");
            }
        }

        return new Result(right, planted, accepted, wrong, Math.Round(seconds, 1), requests.Count);
    }

    public sealed record Result(int Right, int PlantedCaught, int TrueAccepted, IReadOnlyList<string> Wrong, double Seconds, int Requests)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Right}/20 ({TrueAccepted}/14 true accepted, {PlantedCaught}/6 planted caught)");
    }
}
