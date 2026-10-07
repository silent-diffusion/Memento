namespace Memento.Documents.Model.Modules;

/// <summary>Every grounding rule id. The generation pipeline (M4d) and the grounding validator key their checks on these.</summary>
public static class GroundingRules
{
    public const string ClaimRequiresTimestamp = "claimRequiresTimestamp";
    public const string ActionItemRequiresCommitment = "actionItemRequiresCommitment";
    public const string OwnerOnlyIfStated = "ownerOnlyIfStated";
    public const string DeadlineOnlyIfStated = "deadlineOnlyIfStated";
    public const string ParticipantsFromDetails = "participantsFromDetails";
    public const string AgendaReportsNotReached = "agendaReportsNotReached";
    public const string DecisionRequiresAgreement = "decisionRequiresAgreement";
    public const string QuoteIsVerbatim = "quoteIsVerbatim";
    public const string QuestionsUnansweredOnly = "questionsUnansweredOnly";
    public const string TimesFromTranscript = "timesFromTranscript";
    public const string StatedOrFromDetails = "statedOrFromDetails";
    public const string NotDiscussedWhenMissing = "notDiscussedWhenMissing";
    public const string InstructionsCannotOverride = "instructionsCannotOverride";
    public const string TranscriptVerbatim = "transcriptVerbatim";
    public const string AnnotationsVerbatim = "annotationsVerbatim";
    public const string DetailsVerbatim = "detailsVerbatim";
    public const string UserTextUnchanged = "userTextUnchanged";

    public static IReadOnlyList<GroundingRule> All { get; } =
    [
        new(ClaimRequiresTimestamp, "Each statement that reports a decision, action, quote or conclusion carries a timestamp that cites a real transcript moment."),
        new(ActionItemRequiresCommitment, "Action items list only commitments actually made in the recording, each with an in-transcript source."),
        new(OwnerOnlyIfStated, "An owner is given only if one was stated; otherwise the item says no owner was named."),
        new(DeadlineOnlyIfStated, "A deadline is given only if one was stated; otherwise the item says no date was given."),
        new(ParticipantsFromDetails, "Participants come from the recording details and the identified speakers, never from inference."),
        new(AgendaReportsNotReached, "Agenda items that were not reached or not discussed are reported as such instead of inventing coverage."),
        new(DecisionRequiresAgreement, "A decision is listed only where the moment of agreement can be cited in the transcript."),
        new(QuoteIsVerbatim, "Quotes reproduce the transcript's words exactly, with the speaker and the time."),
        new(QuestionsUnansweredOnly, "Open questions are questions raised in the recording and not answered in it."),
        new(TimesFromTranscript, "Every time shown is a real transcript time."),
        new(StatedOrFromDetails, "The content comes from the recording details or was stated in the recording; otherwise it says not discussed."),
        new(NotDiscussedWhenMissing, "When the recording holds nothing for the module it says not discussed instead of inventing content."),
        new(InstructionsCannotOverride, "Per-module instructions can constrain tone and length but never override the grounding rules."),
        new(TranscriptVerbatim, "The transcript as stored, with speakers and times, placed without AI."),
        new(AnnotationsVerbatim, "Chapters, highlights and notes as they are in the project, with their times, placed without AI."),
        new(DetailsVerbatim, "Taken from the recording details as entered, without AI."),
        new(UserTextUnchanged, "The text the user wrote, kept unchanged; no AI is involved."),
    ];

    public static GroundingRule? Find(string id) => All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
}
