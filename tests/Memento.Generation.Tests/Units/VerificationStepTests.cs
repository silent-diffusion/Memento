using System.Text.Json;
using Memento.AI.Payload;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Units;

/// <summary>The graded verifier's answers, "partly" kept only as a shorter summary point, and agenda coverage from the items' own words.</summary>
public sealed class VerificationStepTests
{
    private static readonly ComposedPayload Payload = PayloadComposer.Compose(SyntheticMeeting.Material().ToPayloadInputs(null), SyntheticMeeting.AllInputs);

    private static readonly TranscriptIndex Transcript = new(Payload.TranscriptLines);

    private static int D3 => SyntheticMeeting.LinesOf("D3")[0];

    [Theory]
    [InlineData("""{"reason":"r","verdict":"supported","supported_part":null}""", VerifyAnswer.Supported)]
    [InlineData("""{"reason":"r","verdict":"partly","supported_part":"x y"}""", VerifyAnswer.Partly)]
    [InlineData("""{"reason":"r","verdict":"not supported","supported_part":null}""", VerifyAnswer.NotSupported)]
    [InlineData("""{"reason":"r","supported":true}""", VerifyAnswer.Supported)]
    [InlineData("""{"reason":"r","supported":false}""", VerifyAnswer.NotSupported)]
    public void AnswersAreReadInBothShapes(string json, string grade)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(grade, VerifyPrompts.ParseSingle(document.RootElement)!.Grade);
    }

    [Fact]
    public void AnUnreadableGradeIsNoAnswer()
    {
        using var document = JsonDocument.Parse("""{"reason":"r","verdict":"probably"}""");

        Assert.Null(VerifyPrompts.ParseSingle(document.RootElement));
    }

    [Fact]
    public void APartlySupportedSummaryPointIsKeptWithoutItsUnsupportedDetail()
    {
        var claim = Point("The team agreed to use last write wins with a conflict log for offline mode in version 3.2.", D3);
        var question = VerifyPrompts.Questions(claim, _ => null).Single();

        GenerationPipeline.Apply(question, new VerifyAnswer(VerifyAnswer.Partly, "Offline mode is not named.", "The team agreed to use last write wins with a conflict log in version 3.2."), Transcript);

        Assert.Equal(Verdicts.Supported, claim.Verdict);
        Assert.Equal("The team agreed to use last write wins with a conflict log in version 3.2.", claim.Text);
        Assert.Contains(claim.Notes, n => n.Contains("shortened", StringComparison.Ordinal));
    }

    [Fact]
    public void APartThatAddsWordsOfItsOwnIsNotKept()
    {
        var claim = Point("The team agreed to use last write wins for version 3.2.", D3);
        var question = VerifyPrompts.Questions(claim, _ => null).Single();

        GenerationPipeline.Apply(question, new VerifyAnswer(VerifyAnswer.Partly, "r", "The team agreed to rewrite the sync engine in Rust."), Transcript);

        Assert.Equal(Verdicts.Unsupported, claim.Verdict);
        Assert.Equal("The team agreed to use last write wins for version 3.2.", claim.Text);
    }

    [Fact]
    public void PartlyIsNotSupportedForADecisionOrAnOwner()
    {
        var decision = new Claim { Family = ModuleTask.Commitments, Kind = ClaimKinds.Decision, Text = "Release 3.2 will ship on November 19.", Line = SyntheticMeeting.LinesOf("D1")[0] };
        var action = new Claim { Family = ModuleTask.Commitments, Kind = ClaimKinds.Action, Text = "Deliver the final pricing page mockups.", Owner = "Luis Brandt", Line = SyntheticMeeting.LinesOf("A2")[0] };
        var partly = new VerifyAnswer(VerifyAnswer.Partly, "r", "Release 3.2 will ship.");

        GenerationPipeline.Apply(VerifyPrompts.Questions(decision, _ => null).Single(), partly, Transcript);
        var questions = VerifyPrompts.Questions(action, _ => null).ToList();
        GenerationPipeline.Apply(questions[0], new VerifyAnswer(VerifyAnswer.Supported, "r", null), Transcript);
        GenerationPipeline.Apply(questions[1], partly, Transcript);

        Assert.Equal(Verdicts.Unsupported, decision.Verdict);
        Assert.Equal("Release 3.2 will ship on November 19.", decision.Text);
        Assert.Equal(Verdicts.Supported, action.Verdict);
        Assert.Equal(Verdicts.Unsupported, action.OwnerVerdict);
    }

    [Fact]
    public void NoAnswerLeavesTheClaimUnchecked()
    {
        var claim = Point("Something.", D3);

        GenerationPipeline.Apply(VerifyPrompts.Questions(claim, _ => null).Single(), null, Transcript);

        Assert.Equal(Verdicts.NotChecked, claim.Verdict);
    }

    [Fact]
    public void AnAgendaItemTheChairAnnouncesIsACandidateAndItemsNeverDiscussedAreNot()
    {
        var agenda = SyntheticMeeting.Agenda.Select((a, i) => new AgendaItem("a" + (i + 1), a, false, false, null)).ToList();
        var lines = Payload.TranscriptLines.Select(l => (l, 0)).ToList();

        var candidates = AgendaMatcher.Candidates(agenda, lines, []);

        // "Next, the offline mode backlog." names every word of item 2.
        var offline = candidates.Where(c => c.AgendaItem == 2).ToList();
        Assert.NotEmpty(offline);
        Assert.Contains(offline, c => Transcript.Find(c.Line)!.Text.Contains("offline mode backlog", StringComparison.Ordinal));
        Assert.All(candidates, c => Assert.Equal(ClaimKinds.Agenda, c.Kind));
        Assert.True(candidates.GroupBy(c => c.AgendaItem).All(g => g.Count() <= AgendaMatcher.PerItem));

        // Accessibility audit results and the Q1 hiring plan are never discussed: no candidate names them.
        Assert.DoesNotContain(candidates, c => SyntheticMeeting.AgendaNotDiscussed.Contains(c.AgendaItem!.Value));
    }

    [Fact]
    public void AStatementTheOtherPassesCitedCountsTowardsAnAgendaItem()
    {
        var agenda = new List<AgendaItem> { new("a1", "Weekend support coverage", false, false, null) };
        var line = SyntheticMeeting.LinesOf("F2")[0];
        var cited = new Claim { Family = "points:m05", Kind = ClaimKinds.Point, Text = "Weekend support coverage was discussed but not decided.", Line = line, Quote = null };

        var candidates = AgendaMatcher.Candidates(agenda, Payload.TranscriptLines.Select(l => (l, 0)).ToList(), [cited]);

        Assert.Contains(candidates, c => c.Line == line);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void AnItemNeedsMostOfItsWords(int words, int needed) => Assert.Equal(needed, AgendaMatcher.Needed(words));

    private static Claim Point(string text, int line) => new() { Family = "points:m05", Kind = ClaimKinds.Point, Text = text, Line = line };
}
