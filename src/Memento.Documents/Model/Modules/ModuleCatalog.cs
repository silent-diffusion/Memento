using R = Memento.Documents.Model.Modules.GroundingRules;

namespace Memento.Documents.Model.Modules;

/// <summary>
/// The module catalog the Builder palette, the preview skeletons and the generation pipeline all read. The built-in
/// entries follow PRODUCT-SPEC "Document Modules" and DESIGN.md §10; <see cref="With"/> adds or replaces entries
/// (the module system is extensible). Entries are listed in palette order within each group.
/// </summary>
public sealed class ModuleCatalog
{
    private readonly Dictionary<string, ModuleDefinition> _byId;

    public ModuleCatalog(IEnumerable<ModuleDefinition> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        All = modules.ToList();
        _byId = new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal);
        foreach (var module in All)
        {
            if (!_byId.TryAdd(module.Id, module))
            {
                throw new ArgumentException($"The module id \"{module.Id}\" is listed twice.", nameof(modules));
            }
        }
    }

    /// <summary>The built-in catalog.</summary>
    public static ModuleCatalog Default { get; } = new(BuiltIn());

    public IReadOnlyList<ModuleDefinition> All { get; }

    public ModuleDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    /// <exception cref="KeyNotFoundException">No module has that id.</exception>
    public ModuleDefinition Get(string id) =>
        Find(id) ?? throw new KeyNotFoundException($"The document module \"{id}\" is not in the catalog.");

    public IReadOnlyList<ModuleDefinition> InGroup(PaletteGroup group) => All.Where(m => m.Group == group).ToList();

    /// <summary>A catalog with <paramref name="module"/> added, or replacing the entry with the same id.</summary>
    public ModuleCatalog With(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var list = All.Where(m => !string.Equals(m.Id, module.Id, StringComparison.Ordinal)).ToList();
        var index = All.ToList().FindIndex(m => string.Equals(m.Id, module.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            list.Insert(index, module);
        }
        else
        {
            list.Add(module);
        }

        return new ModuleCatalog(list);
    }

    private static List<ModuleDefinition> BuiltIn() =>
    [
        // Structure
        new()
        {
            Id = ModuleIds.Title, DisplayName = "Title", Group = PaletteGroup.Structure, Shape = ContentShape.Text,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Data, GroundingRules = [R.DetailsVerbatim],
            DefaultInstructions = "The recording's title as entered in its details.",
        },
        new()
        {
            Id = ModuleIds.Summary, DisplayName = "Summary", Group = PaletteGroup.Structure, Shape = ContentShape.Paragraph,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.ClaimRequiresTimestamp, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "A neutral summary of what was said, in the order it was said.",
        },
        new()
        {
            Id = ModuleIds.ExecutiveSummary, DisplayName = "Executive summary", Group = PaletteGroup.Structure, Shape = ContentShape.Paragraph,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.ClaimRequiresTimestamp, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "Three sentences. Decisions first, then risks.",
        },
        new()
        {
            Id = ModuleIds.MeetingPurpose, DisplayName = "Meeting purpose", Group = PaletteGroup.Structure, Shape = ContentShape.LabelValue,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Ai, Labels = ["Purpose"],
            GroundingRules = [R.StatedOrFromDetails, R.InstructionsCannotOverride],
            DefaultInstructions = "One line, taken from the recording details.",
        },
        new()
        {
            Id = ModuleIds.Participants, DisplayName = "Participants", Group = PaletteGroup.Structure, Shape = ContentShape.Chips,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Data, GroundingRules = [R.ParticipantsFromDetails],
            DefaultInstructions = "Names from the recording details and the identified speakers.",
        },
        new()
        {
            Id = ModuleIds.Agenda, DisplayName = "Agenda", Group = PaletteGroup.Structure, Shape = ContentShape.List,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.AgendaReportsNotReached, R.ClaimRequiresTimestamp, R.InstructionsCannotOverride],
            DefaultInstructions = "As imported, with a note on items not reached.",
        },
        new()
        {
            Id = ModuleIds.Discussion, DisplayName = "Discussion summary", Group = PaletteGroup.Structure, Shape = ContentShape.Paragraph,
            DefaultLength = ModuleLength.Long, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.ClaimRequiresTimestamp, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "One short paragraph per agenda item, neutral tone.",
        },
        new()
        {
            Id = ModuleIds.Decisions, DisplayName = "Decisions", Group = PaletteGroup.Structure, Shape = ContentShape.List,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.DecisionRequiresAgreement, R.ClaimRequiresTimestamp, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "Bulleted. Quote the moment of agreement with a timestamp.",
        },
        new()
        {
            Id = ModuleIds.ActionItems, DisplayName = "Action items", Group = PaletteGroup.Structure, Shape = ContentShape.Table,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            Columns = ["Action", "Owner", "Due"], ColumnWidths = [2, 1, 1],
            GroundingRules = [R.ActionItemRequiresCommitment, R.OwnerOnlyIfStated, R.DeadlineOnlyIfStated, R.ClaimRequiresTimestamp, R.InstructionsCannotOverride],
            DefaultInstructions = "Table: action, owner, deadline. Only commitments actually made.",
        },
        new()
        {
            Id = ModuleIds.OpenQuestions, DisplayName = "Open questions", Group = PaletteGroup.Structure, Shape = ContentShape.List,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Ai,
            GroundingRules = [R.QuestionsUnansweredOnly, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "Questions raised but not answered.",
        },
        new()
        {
            Id = ModuleIds.NextMeeting, DisplayName = "Next meeting", Group = PaletteGroup.Structure, Shape = ContentShape.LabelValue,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Ai, Labels = ["When", "Agenda"],
            GroundingRules = [R.StatedOrFromDetails, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "Date, time and proposed agenda if mentioned.",
        },

        // Detail
        new()
        {
            Id = ModuleIds.Topic, DisplayName = "Topic", Group = PaletteGroup.Detail, Shape = ContentShape.Paragraph,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.ClaimRequiresTimestamp, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "What was said about one topic, with who said it.",
        },
        new()
        {
            Id = ModuleIds.Owner, DisplayName = "Owner", Group = PaletteGroup.Detail, Shape = ContentShape.LabelValue,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            // One pair per named owner: the label is the owner's name as it appears in the transcript.
            Labels = ["Owner"],
            GroundingRules = [R.OwnerOnlyIfStated, R.ActionItemRequiresCommitment, R.ClaimRequiresTimestamp, R.InstructionsCannotOverride],
            DefaultInstructions = "Each named owner and what they committed to.",
        },
        new()
        {
            Id = ModuleIds.Deadline, DisplayName = "Deadline", Group = PaletteGroup.Detail, Shape = ContentShape.Table,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            Columns = ["Due", "Action"], ColumnWidths = [1, 3],
            GroundingRules = [R.DeadlineOnlyIfStated, R.ActionItemRequiresCommitment, R.ClaimRequiresTimestamp, R.InstructionsCannotOverride],
            DefaultInstructions = "Stated deadlines in date order.",
        },
        new()
        {
            Id = ModuleIds.Quote, DisplayName = "Quote", Group = PaletteGroup.Detail, Shape = ContentShape.Quote,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.QuoteIsVerbatim, R.ClaimRequiresTimestamp, R.InstructionsCannotOverride],
            DefaultInstructions = "One remark that captures the meeting, word for word.",
        },
        new()
        {
            Id = ModuleIds.Highlight, DisplayName = "Highlight", Group = PaletteGroup.Detail, Shape = ContentShape.Quote,
            DefaultLength = ModuleLength.Short, Source = ModuleSource.Data, DefaultLinkToTranscript = true,
            GroundingRules = [R.AnnotationsVerbatim, R.TimesFromTranscript],
            DefaultInstructions = "The highlights marked in the recording, with their notes.",
        },
        new()
        {
            Id = ModuleIds.Chapter, DisplayName = "Chapter", Group = PaletteGroup.Detail, Shape = ContentShape.Timeline,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Data, DefaultLinkToTranscript = true,
            GroundingRules = [R.AnnotationsVerbatim, R.TimesFromTranscript],
            DefaultInstructions = "The recording's chapters with their start times.",
        },
        new()
        {
            Id = ModuleIds.Timeline, DisplayName = "Timeline", Group = PaletteGroup.Detail, Shape = ContentShape.Timeline,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.TimesFromTranscript, R.ClaimRequiresTimestamp, R.InstructionsCannotOverride],
            DefaultInstructions = "The key moments in time order.",
        },
        new()
        {
            Id = ModuleIds.FollowUpEmail, DisplayName = "Follow-up email", Group = PaletteGroup.Detail, Shape = ContentShape.Paragraph,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai,
            GroundingRules = [R.ActionItemRequiresCommitment, R.OwnerOnlyIfStated, R.DeadlineOnlyIfStated, R.InstructionsCannotOverride],
            DefaultInstructions = "A short email to the participants: decisions, actions and the next meeting.",
        },
        new()
        {
            Id = ModuleIds.Notes, DisplayName = "Notes", Group = PaletteGroup.Detail, Shape = ContentShape.List,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Data, DefaultLinkToTranscript = true,
            GroundingRules = [R.AnnotationsVerbatim],
            DefaultInstructions = "The notes written during and after the recording.",
        },
        new()
        {
            Id = ModuleIds.FullTranscript, DisplayName = "Full transcript", Group = PaletteGroup.Detail, Shape = ContentShape.Transcript,
            DefaultLength = ModuleLength.Long, Source = ModuleSource.Data, GroundingRules = [R.TranscriptVerbatim],
            DefaultInstructions = "The entire transcript with speakers and timestamps. No AI is involved.",
        },

        // Custom
        new()
        {
            Id = ModuleIds.CustomText, DisplayName = "Custom text", Group = PaletteGroup.Custom, Shape = ContentShape.Text,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.User, GroundingRules = [R.UserTextUnchanged],
            DefaultInstructions = "Your own text, placed as written.",
        },
        new()
        {
            Id = ModuleIds.CustomAi, DisplayName = "Custom AI section", Group = PaletteGroup.Custom, Shape = ContentShape.Paragraph,
            DefaultLength = ModuleLength.Medium, Source = ModuleSource.Ai, DefaultLinkToTranscript = true,
            GroundingRules = [R.ClaimRequiresTimestamp, R.NotDiscussedWhenMissing, R.InstructionsCannotOverride],
            DefaultInstructions = "Describe what this section should contain.",
        },
    ];
}
