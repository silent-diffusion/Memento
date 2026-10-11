using Memento.AI.Payload;
using Memento.Documents.Export;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;
using Memento.Documents.Templates;
using Memento.Generation.Documents;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Pipeline;

/// <summary>
/// The Follow-up email module (2.0) through the whole pipeline on the synthetic meeting: its recap has its own map pass,
/// its decisions and action items share the decisions-and-actions pass, every statement is verified against the line it
/// cites, the planted false claims never reach it, and the fixed sentences follow its tone and sign-off.
/// </summary>
public sealed class FollowUpEmailTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 5, 16, 0, 0, TimeSpan.FromHours(1));

    private static DocumentTemplate EmailTemplate(EmailOptions? email, ModuleLength length = ModuleLength.Medium, bool withDecisions = false) => BuiltInTemplates.MeetingMinutes with
    {
        Rows =
        [
            .. withDecisions ? new[] { new TemplateRow { Modules = [new TemplateModule { Id = "d1", Type = ModuleIds.Decisions, Title = "Decisions", Length = ModuleLength.Medium, LinkToTranscript = true }] } } : [],
            new TemplateRow
            {
                Modules =
                [
                    new TemplateModule
                    {
                        Id = "e1",
                        Type = ModuleIds.FollowUpEmail,
                        Title = "Follow-up email",
                        Instructions = ModuleCatalog.Default.Find(ModuleIds.FollowUpEmail)!.DefaultInstructions,
                        Length = length,
                        Email = email,
                    },
                ],
            },
        ],
    };

    [Fact]
    public async Task TheEmailIsWrittenFromVerifiedClaimsOnly()
    {
        var (outcome, provider) = await RunAsync(EmailTemplate(new EmailOptions { Tone = EmailOptions.Warm, SignOff = "Dana" }));

        // Map: the shared decisions-and-actions pass and the email's own recap pass, per chunk; then verification.
        Assert.Contains(provider.Requests, r => r.Purpose == "map.commitments#1");
        Assert.Contains(provider.Requests, r => r.Purpose == "map.followUpEmail#1");
        var recapRequest = provider.Requests.First(r => r.Purpose == "map.followUpEmail#1");
        Assert.Contains("the recap of a follow-up email", recapRequest.System, StringComparison.Ordinal);
        Assert.Contains("at most 3 short, factual statements", recapRequest.System, StringComparison.Ordinal);
        Assert.Contains(provider.Requests, r => r.Purpose.StartsWith("verify.", StringComparison.Ordinal));

        var claims = outcome.Claims.Where(c => c.ModuleId == "e1").ToList();
        var kept = claims.Where(c => c.Kept).ToList();
        Assert.Contains(kept, c => c.Kind == ClaimKinds.Point);
        Assert.Contains(kept, c => c.Kind == ClaimKinds.Decision);
        Assert.Contains(kept, c => c.Kind == ClaimKinds.Action);
        Assert.True(kept.Count(c => c.Kind == ClaimKinds.Point) <= ModuleTask.RecapCap(ModuleLength.Medium));
        Assert.All(kept, c => Assert.Equal(Verdicts.Supported, c.Verdict));
        Assert.All(kept, c => Assert.NotNull(c.SegmentId));
        Assert.All(MeetingProvider.PlantedDecisions, planted => Assert.DoesNotContain(kept, c => c.Text == planted));
        Assert.DoesNotContain(kept, c => c.Text == MeetingProvider.FabricatedPoint);
        Assert.Contains(claims, c => !c.Kept && c.Text == MeetingProvider.FabricatedPoint);

        // Owners are the transcript's names; an action item without one says so.
        var actions = kept.Where(c => c.Kind == ClaimKinds.Action).ToList();
        Assert.Contains(actions, c => c.Owner == "Mei Tanaka" && c.Text.Contains("mockups", StringComparison.Ordinal));
        Assert.All(actions.Where(c => c.Owner is not null), c => Assert.Contains(c.Owner!, SyntheticMeeting.Speakers.Select(s => s.Name)));

        var record = outcome.Modules.Single(m => m.ModuleId == "e1");
        Assert.Equal("ai", record.Source);
        Assert.Equal(claims.Count, record.Claims);
        Assert.False(record.NotDiscussed);
    }

    [Fact]
    public async Task TheEmailHasRecipientsSubjectGreetingRecapDecisionsActionsAndClosing()
    {
        var (outcome, _) = await RunAsync(EmailTemplate(new EmailOptions { Tone = EmailOptions.Neutral, SignOff = "Dana" }));

        var module = outcome.Rows.SelectMany(r => r.Modules).Single(m => m.Id == "e1");
        Assert.Equal(ProvenanceKind.Ai, module.Provenance.Kind);
        var header = Assert.IsType<LabelValueBlock>(module.Blocks[0]);
        Assert.Equal([FollowUpEmailWriter.ToLabel, FollowUpEmailWriter.SubjectLabel], header.Pairs.Select(p => p.Label));
        Assert.Equal(string.Join("; ", SyntheticMeeting.Speakers.Select(s => s.Name)), Text(header.Pairs[0].Runs));
        Assert.Equal("Follow-up: Ledgerly weekly product sync (5 October 2026)", Text(header.Pairs[1].Runs));

        var text = ModulePlainText.Write(module);
        var lines = text.Split("\r\n");
        Assert.Equal("To: " + string.Join("; ", SyntheticMeeting.Speakers.Select(s => s.Name)), lines[0]);
        Assert.Contains("Thank you for joining Ledgerly weekly product sync on Monday 5 October. Here is a summary of what we discussed and agreed.", lines);
        Assert.Contains("Summary", lines);
        Assert.Contains("Decisions", lines);
        Assert.Contains("Action items", lines);
        Assert.Contains(lines, l => l.StartsWith("- Mei Tanaka: ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("- Luis Brandt: ", StringComparison.Ordinal) && l.Contains("by Friday", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("- ", StringComparison.Ordinal) && l.EndsWith("(no owner named).", StringComparison.Ordinal));
        Assert.Equal(["Please reply if anything here does not match your notes.", string.Empty, "Best regards,", string.Empty, "Dana", string.Empty], lines[^6..]);
        Assert.DoesNotContain('!', text);

        // Decisions come before action items, the recap before both, the greeting before the recap.
        Assert.True(Array.IndexOf(lines, "Summary") < Array.IndexOf(lines, "Decisions"));
        Assert.True(Array.IndexOf(lines, "Decisions") < Array.IndexOf(lines, "Action items"));
    }

    [Fact]
    public async Task TheEmailSharesTheDecisionsPassWithTheDecisionsModule()
    {
        var (outcome, provider) = await RunAsync(EmailTemplate(null, withDecisions: true));

        Assert.Equal(provider.Requests.Count(r => r.Purpose.StartsWith("map.commitments#", StringComparison.Ordinal)), outcome.Chunks);
        var decisions = outcome.Claims.Where(c => c.ModuleId == "d1" && c.Kept).Select(c => c.Text).ToHashSet();
        var emailed = outcome.Claims.Where(c => c.ModuleId == "e1" && c.Kept && c.Kind == ClaimKinds.Decision).Select(c => c.Text).ToHashSet();
        Assert.Equal(decisions, emailed);
    }

    [Theory]
    [InlineData(EmailOptions.Neutral, "Hello everyone,", "Best regards,")]
    [InlineData(EmailOptions.Warm, "Hi everyone,", "Warm regards,")]
    [InlineData(EmailOptions.Brief, "Hi all,", "Thanks,")]
    public void TheToneSetsTheFixedSentencesOnly(string tone, string greeting, string signOff)
    {
        string[] many = ["Ana Ruiz", "Ben Ode", "Cai Li", "Dev Shah", "Eli Moss"];
        Assert.Equal(greeting, FollowUpEmailWriter.Greeting(tone, many));
        Assert.Equal(signOff, FollowUpEmailWriter.SignOffWords(tone));
        Assert.DoesNotContain('!', FollowUpEmailWriter.Opening(tone, "Weekly sync", Recorded) + FollowUpEmailWriter.Closing(tone));
    }

    [Fact]
    public void AFewPeopleAreGreetedByFirstName()
    {
        Assert.Equal("Hello Ana, Ben and Cai,", FollowUpEmailWriter.Greeting(EmailOptions.Neutral, ["Ana Ruiz", "Ben Ode", "Cai Li"]));
        Assert.Equal("Hi Ana,", FollowUpEmailWriter.Greeting(EmailOptions.Brief, ["Ana Ruiz"]));
        Assert.Equal("Hi all,", FollowUpEmailWriter.Greeting(EmailOptions.Brief, ["Ana Ruiz", "Ben Ode"]));
        Assert.Equal("Hello everyone,", FollowUpEmailWriter.Greeting(EmailOptions.Neutral, []));
        Assert.Equal("Recap: Weekly sync", FollowUpEmailWriter.Subject(EmailOptions.Brief, "Weekly sync", Recorded));
        Assert.Equal("Follow-up: our meeting", FollowUpEmailWriter.Subject(EmailOptions.Neutral, null, null));
    }

    [Fact]
    public void WithNothingAgreedTheEmailSaysSoAndInventsNothing()
    {
        var facts = new GenerationFacts("Weekly sync", null, [], [], []) { RecordedAt = Recorded };

        var blocks = FollowUpEmailWriter.Write([], facts, new EmailOptions { Tone = EmailOptions.Brief }, _ => []);

        var text = ModulePlainText.Write(new ModuleBlock { Id = "e1", Type = ModuleIds.FollowUpEmail, Title = "Follow-up email", Blocks = blocks });
        Assert.Contains("To: " + FollowUpEmailWriter.NoRecipients, text, StringComparison.Ordinal);
        Assert.Contains(FollowUpEmailWriter.NothingAgreed, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Summary", text, StringComparison.Ordinal);
        Assert.EndsWith("Thanks,\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABriefEmailHasAShorterRecap()
    {
        var (outcome, _) = await RunAsync(EmailTemplate(new EmailOptions { Tone = EmailOptions.Brief }, ModuleLength.Short));

        Assert.True(outcome.Claims.Count(c => c.ModuleId == "e1" && c.Kept && c.Kind == ClaimKinds.Point) <= ModuleTask.RecapCap(ModuleLength.Short));
        var module = outcome.Rows.SelectMany(r => r.Modules).Single(m => m.Id == "e1");
        var text = ModulePlainText.Write(module);
        Assert.Contains("Subject: Recap: Ledgerly weekly product sync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nSummary\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmailExportsAsItsOwnSectionInMarkdown()
    {
        var facts = new GenerationFacts("Weekly sync", null, ["Ana Ruiz"], [], []) { RecordedAt = Recorded };
        var decision = new Claim { Family = ModuleTask.Commitments, Kind = ClaimKinds.Decision, Text = "ship on Friday", Line = 3 };
        var blocks = FollowUpEmailWriter.Write([decision], facts, null, _ => []);
        var document = new Document { Id = "d", Title = "Weekly sync", Rows = [new DocumentRow { Modules = [new ModuleBlock { Id = "e1", Type = ModuleIds.FollowUpEmail, Title = "Follow-up email", Blocks = blocks }] }] };

        var markdown = new MarkdownExporter().Export(document);

        Assert.Contains("## Follow-up email\n\n**To:** Ana Ruiz  \n**Subject:** Follow-up: Weekly sync (5 October 2026)\n\nHello Ana,", markdown, StringComparison.Ordinal);
        Assert.Contains("**Decisions**\n\n- Ship on Friday.\n", markdown, StringComparison.Ordinal);
    }

    private static string Text(IEnumerable<Run> runs) => string.Concat(runs.Select(r => r.Text));

    private static async Task<(PipelineOutcome Outcome, MeetingProvider Provider)> RunAsync(DocumentTemplate template)
    {
        var meeting = new MeetingProvider();
        var material = SyntheticMeeting.Material();
        var payload = PayloadComposer.Compose(material.ToPayloadInputs(null), SyntheticMeeting.AllInputs);
        var facts = new GenerationFacts(material.Details.Title, null, DataModuleComposer.Participants(material), material.Details.Agenda.Items, []) { RecordedAt = Recorded };
        var input = new PipelineInput(template, material, payload, SyntheticMeeting.AllInputs, meeting, facts, 3000, 1400, true, GenerationPipeline.LocalVerifyBatchSize);
        var outcome = await new GenerationPipeline(ModuleCatalog.Default).RunAsync(input, null, CancellationToken.None);
        return (outcome, meeting);
    }
}
