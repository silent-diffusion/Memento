using Memento.Documents.Model.Modules;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class ModuleCatalogTests
{
    /// <summary>Every module named by PRODUCT-SPEC "Document Modules" and the Builder palette (DESIGN.md §10).</summary>
    private static readonly string[] SpecModules =
    [
        "Title", "Summary", "Executive summary", "Participants", "Agenda", "Topic", "Discussion summary", "Decisions", "Action items",
        "Owner", "Deadline", "Open questions", "Quote", "Highlight", "Chapter", "Timeline", "Follow-up email", "Next meeting",
        "Meeting purpose", "Notes", "Full transcript", "Custom text", "Custom AI section",
    ];

    private static ModuleCatalog Catalog => ModuleCatalog.Default;

    [Fact]
    public void EverySpecModuleIsInTheCatalog()
    {
        var names = Catalog.All.Select(m => m.DisplayName).ToHashSet(StringComparer.Ordinal);
        Assert.All(SpecModules, name => Assert.Contains(name, names));
        Assert.Equal(SpecModules.Length, Catalog.All.Count);
    }

    [Fact]
    public void TheDesignPaletteGroupsAreKeptInOrder()
    {
        string[] structure = ["Title", "Executive summary", "Participants", "Agenda", "Discussion summary", "Decisions", "Action items", "Open questions", "Next meeting"];
        string[] detail = ["Topic", "Quote", "Highlight", "Chapter", "Timeline", "Follow-up email", "Notes"];
        string[] custom = ["Custom text", "Custom AI section"];
        Assert.Equal(structure, Catalog.InGroup(PaletteGroup.Structure).Select(m => m.DisplayName).Where(structure.Contains));
        Assert.Equal(detail, Catalog.InGroup(PaletteGroup.Detail).Select(m => m.DisplayName).Where(detail.Contains));
        Assert.Equal(custom, Catalog.InGroup(PaletteGroup.Custom).Select(m => m.DisplayName));
    }

    [Fact]
    public void EveryGroundingRuleIsReferencedAndEveryReferenceIsKnown()
    {
        var known = GroundingRules.All.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var referenced = Catalog.All.SelectMany(m => m.GroundingRules).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(referenced.Except(known));
        Assert.Empty(known.Except(referenced));
        Assert.All(Catalog.All, m => Assert.NotEmpty(m.GroundingRules));
        Assert.Equal(known.Count, GroundingRules.All.Count);
        Assert.All(GroundingRules.All, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
    }

    [Theory]
    [InlineData(ModuleIds.ActionItems, ContentShape.Table, ModuleSource.Ai, GroundingRules.ActionItemRequiresCommitment)]
    [InlineData(ModuleIds.Participants, ContentShape.Chips, ModuleSource.Data, GroundingRules.ParticipantsFromDetails)]
    [InlineData(ModuleIds.Agenda, ContentShape.List, ModuleSource.Ai, GroundingRules.AgendaReportsNotReached)]
    [InlineData(ModuleIds.MeetingPurpose, ContentShape.LabelValue, ModuleSource.Ai, GroundingRules.StatedOrFromDetails)]
    [InlineData(ModuleIds.NextMeeting, ContentShape.LabelValue, ModuleSource.Ai, GroundingRules.StatedOrFromDetails)]
    [InlineData(ModuleIds.Quote, ContentShape.Quote, ModuleSource.Ai, GroundingRules.QuoteIsVerbatim)]
    [InlineData(ModuleIds.Highlight, ContentShape.Quote, ModuleSource.Data, GroundingRules.AnnotationsVerbatim)]
    [InlineData(ModuleIds.Timeline, ContentShape.Timeline, ModuleSource.Ai, GroundingRules.TimesFromTranscript)]
    [InlineData(ModuleIds.Chapter, ContentShape.Timeline, ModuleSource.Data, GroundingRules.AnnotationsVerbatim)]
    [InlineData(ModuleIds.FullTranscript, ContentShape.Transcript, ModuleSource.Data, GroundingRules.TranscriptVerbatim)]
    [InlineData(ModuleIds.CustomText, ContentShape.Text, ModuleSource.User, GroundingRules.UserTextUnchanged)]
    [InlineData(ModuleIds.ExecutiveSummary, ContentShape.Paragraph, ModuleSource.Ai, GroundingRules.ClaimRequiresTimestamp)]
    public void ModulesDeclareShapeSourceAndRules(string id, ContentShape shape, ModuleSource source, string rule)
    {
        var module = Catalog.Get(id);
        Assert.Equal(shape, module.Shape);
        Assert.Equal(source, module.Source);
        Assert.Contains(rule, module.GroundingRules);
        Assert.Equal(source == ModuleSource.Ai, module.IsAiGenerated);
    }

    [Fact]
    public void TablesNameTheirColumnsAndLabelValuesTheirLabels()
    {
        Assert.All(Catalog.All.Where(m => m.Shape == ContentShape.Table), m =>
        {
            Assert.NotEmpty(m.Columns);
            Assert.Equal(m.Columns.Count, m.ColumnWidths.Count);
        });
        Assert.Equal(["Action", "Owner", "Due"], Catalog.Get(ModuleIds.ActionItems).Columns);
        Assert.All(Catalog.All.Where(m => m.Shape == ContentShape.LabelValue), m => Assert.NotEmpty(m.Labels));
    }

    [Fact]
    public void OnlyTheFullTranscriptAndSourcesOfDataAvoidTheAi()
    {
        Assert.All(Catalog.All.Where(m => m.Source != ModuleSource.Ai), m => Assert.DoesNotContain(GroundingRules.InstructionsCannotOverride, m.GroundingRules));
        Assert.All(Catalog.All.Where(m => m.Source == ModuleSource.Ai), m => Assert.Contains(GroundingRules.InstructionsCannotOverride, m.GroundingRules));
    }

    [Fact]
    public void TheCatalogIsExtensible()
    {
        var extended = Catalog.With(new ModuleDefinition
        {
            Id = "riskRegister",
            DisplayName = "Risk register",
            Group = PaletteGroup.Detail,
            Shape = ContentShape.Table,
            Source = ModuleSource.Ai,
            Columns = ["Risk", "Owner"],
            ColumnWidths = [3, 1],
            GroundingRules = [GroundingRules.ClaimRequiresTimestamp],
        });
        Assert.Equal(Catalog.All.Count + 1, extended.All.Count);
        Assert.NotNull(extended.Find("riskRegister"));
        Assert.Null(Catalog.Find("riskRegister"));

        var replaced = extended.With(extended.Get(ModuleIds.Notes) with { DisplayName = "My notes" });
        Assert.Equal(extended.All.Count, replaced.All.Count);
        Assert.Equal("My notes", replaced.Get(ModuleIds.Notes).DisplayName);
        Assert.Throws<KeyNotFoundException>(() => Catalog.Get("nope"));
    }
}
