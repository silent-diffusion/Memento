using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;
using Memento.Documents.Templates;
using Memento.Generation.Bridge;
using Memento.Generation.Documents;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Units;

/// <summary>The project document store's version rules, and the data modules placed without AI.</summary>
public sealed class DocumentStoreAndComposerTests : IDisposable
{
    private readonly M4Host _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task VersionsAreKeptByTheTranscriptsRules()
    {
        var id = await _host.CreateMeetingAsync();
        var store = _host.Get<ProjectDocumentStore>();
        const string doc = "d0000000001";

        await store.WriteAsync(id, doc, DocumentChangeReasons.Generated, _ => Document("first"), CancellationToken.None);
        await store.WriteAsync(id, doc, DocumentChangeReasons.Edited, d => d! with { Title = "edit 1" }, CancellationToken.None);
        await store.WriteAsync(id, doc, DocumentChangeReasons.Edited, d => d! with { Title = "edit 2" }, CancellationToken.None);
        await store.WriteAsync(id, doc, DocumentChangeReasons.Renamed, d => d! with { Name = "Minutes" }, CancellationToken.None);
        await store.WriteAsync(id, doc, DocumentChangeReasons.Regenerated, _ => Document("second"), CancellationToken.None);
        var versions = await store.ListVersionsAsync(id, doc, CancellationToken.None);

        // One version for the run of edits (the generated content), one for the regeneration (the edited content).
        Assert.Equal(["edited", "generated"], versions.Select(v => v.Reason));
        Assert.Equal(["edit 2", "first"], versions.Select(v => v.Document.Title));
        var current = await store.LoadAsync(id, doc, CancellationToken.None);
        Assert.Equal(5, current!.Version);
        Assert.Equal(DocumentChangeReasons.Regenerated, current.LastChange!.Reason);

        await store.WriteAsync(id, doc, DocumentChangeReasons.Restored, _ => versions[1].Document, CancellationToken.None);
        Assert.Equal(3, (await store.ListVersionsAsync(id, doc, CancellationToken.None)).Count);
        Assert.Equal("first", (await store.LoadAsync(id, doc, CancellationToken.None))!.Title);

        await _host.Host.Settings.UpdateAsync(s => s with { History = new HistorySettings { KeepVersions = false } }, CancellationToken.None);
        await store.WriteAsync(id, doc, DocumentChangeReasons.Regenerated, _ => Document("third"), CancellationToken.None);
        Assert.Equal(3, (await store.ListVersionsAsync(id, doc, CancellationToken.None)).Count);

        Assert.True(await store.DeleteAsync(id, doc, CancellationToken.None));
        Assert.Empty(await store.ListVersionsAsync(id, doc, CancellationToken.None));
        Assert.Null(await store.LoadAsync(id, doc, CancellationToken.None));
    }

    [Fact]
    public async Task TheFullTranscriptIsTheTranscriptExactly()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.Host.Store.UpdateAnnotationsAsync(id, a => a with { Chapters = [new Chapter("c1", 291_000, "Offline mode", "user")] }, CancellationToken.None);
        var material = await _host.Get<RecordingMaterialLoader>().LoadAsync(id, null, CancellationToken.None);
        var transcript = (await _host.Host.Transcripts.LoadAsync(id, CancellationToken.None))!;

        var block = DataModuleComposer.FullTranscript(material);

        Assert.Equal(transcript.Segments.Count, block.Segments.Count);
        for (var i = 0; i < transcript.Segments.Count; i++)
        {
            var segment = transcript.Segments[i];
            Assert.Equal(segment.Id, block.Segments[i].Id);
            Assert.Equal(segment.Start, block.Segments[i].T);
            Assert.Equal(segment.Text, block.Segments[i].Text);
            Assert.Equal(transcript.Speakers.Single(s => s.Id == segment.Speaker).Name, block.Segments[i].Speaker);
        }

        Assert.Equal(("Offline mode", 291.0), (block.Chapters.Single().Title, block.Chapters.Single().T));

        // And through a document, its JSON and Markdown: nothing reworded.
        var template = new DocumentTemplate { Rows = [new TemplateRow { Modules = [new TemplateModule { Id = "t1", Type = ModuleIds.FullTranscript }] }] };
        var composed = DataModuleComposer.Compose(template.Rows[0].Modules[0], ModuleCatalog.Default.Find(ModuleIds.FullTranscript), material)!;
        var document = new Document { Id = "d1", Title = "T", Rows = [DocumentRow.Of(new ModuleBlock { Id = "t1", Type = ModuleIds.FullTranscript, Title = "Full transcript", Blocks = composed })] };
        var roundTrip = (TranscriptBlock)DocumentJson.Deserialize(DocumentJson.Serialize(document)).Rows[0].Modules[0].Blocks[0];
        Assert.Equal(block.Segments.Select(s => (s.Id, s.Speaker, s.T, s.Text)), roundTrip.Segments.Select(s => (s.Id, s.Speaker, s.T, s.Text)));
        var markdown = new Memento.Documents.Export.MarkdownExporter().Export(document);
        Assert.All(transcript.Segments, s => Assert.Contains(s.Text, markdown, StringComparison.Ordinal));
    }

    [Fact]
    public void ParticipantsComeFromTheDetailsAndNamedSpeakersOnly()
    {
        var material = SyntheticMeeting.Material() with
        {
            Manifest = SyntheticMeeting.Manifest() with { Details = SyntheticMeeting.Manifest().Details with { Participants = ["Dana Okafor", "Priya Nair"] } },
            Transcript = SyntheticMeeting.Transcript() with
            {
                Speakers = [new Speaker("spk1", "Dana Okafor", true, 1, 0), new Speaker("spk2", "Speaker 2", false, 2, 0), new Speaker("spk3", "Mei Tanaka", true, 3, 0)],
            },
        };

        Assert.Equal(["Dana Okafor", "Priya Nair", "Mei Tanaka"], DataModuleComposer.Participants(material));
        var chips = DataModuleComposer.Compose(new TemplateModule { Id = "p", Type = ModuleIds.Participants }, ModuleCatalog.Default.Find(ModuleIds.Participants), material)!;
        Assert.Equal(["Dana Okafor", "Priya Nair", "Mei Tanaka"], Assert.IsType<ChipsBlock>(Assert.Single(chips)).Items);
    }

    [Fact]
    public void CustomTextIsPlacedAsWrittenAndAnAiModuleIsNotComposed()
    {
        var material = SyntheticMeeting.Material();

        var text = DataModuleComposer.Compose(new TemplateModule { Id = "c", Type = ModuleIds.CustomText, CustomText = "First paragraph.\n\nSecond." }, ModuleCatalog.Default.Find(ModuleIds.CustomText), material)!;
        Assert.Equal(["First paragraph.", "Second."], text.Cast<ParagraphBlock>().Select(p => p.Runs[0].Text));
        Assert.Null(DataModuleComposer.Compose(new TemplateModule { Id = "d", Type = ModuleIds.Decisions }, ModuleCatalog.Default.Find(ModuleIds.Decisions), material));
    }

    [Fact]
    public void ABuiltInWithoutTheNewerSwitchesKeepsTheirDefaults()
    {
        // The built-in files predate "details" and "previousDocuments".
        Assert.True(BuiltInTemplates.MeetingMinutes.Inputs.Details);
        Assert.False(BuiltInTemplates.MeetingMinutes.Inputs.PreviousDocuments);
        Assert.True(M4Mapping.Selection(BuiltInTemplates.MeetingMinutes.Inputs).Details);
    }

    [Fact]
    public async Task EveryM4MethodIsRegistered()
    {
        await Task.CompletedTask;
        var names = typeof(Core.Bridge.BridgeMethodNames).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)
            .Where(n => n.Split('.')[0] is "modules" or "templates" or "styles" or "providers" or "generation" or "documents")
            .ToList();

        // 32 at M4, documents.copy after 1.2.0.
        Assert.Equal(33, names.Count);
        Assert.All(names, n => Assert.Contains(n, _host.Host.Router.MethodNames));
    }

    [Fact]
    public async Task BuiltInsAreNeverSavedOverAndAStyleInUseIsKept()
    {
        var builtIn = await _host.ResultAsync("templates.get", new { templateId = "meeting-minutes" });
        var saved = await _host.ResultAsync("templates.save", new { template = builtIn });
        Assert.NotEqual("meeting-minutes", saved.GetProperty("id").GetString());
        Assert.Equal("Meeting minutes (copy)", saved.GetProperty("name").GetString());
        Assert.False(saved.GetProperty("builtIn").GetBoolean());
        Assert.Equal(Core.Bridge.DomainErrorCodes.TemplatesBuiltIn, (await _host.ErrorAsync("templates.delete", new { templateId = "meeting-minutes" })).GetProperty("code").GetString());
        Assert.Contains(_host.Events("templates.changed"), _ => true);

        var style = await _host.ResultAsync("styles.duplicate", new { styleId = "minimal" });
        var styleId = style.GetProperty("id").GetString()!;
        var template = System.Text.Json.Nodes.JsonNode.Parse(saved.GetRawText())!;
        template["styleId"] = styleId;
        await _host.ResultAsync("templates.save", new { template });
        var inUse = await _host.ErrorAsync("styles.delete", new { styleId });
        Assert.Equal(Core.Bridge.DomainErrorCodes.StylesInUse, inUse.GetProperty("code").GetString());
        Assert.Contains("Meeting minutes (copy)", inUse.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(Core.Bridge.DomainErrorCodes.StylesBuiltIn, (await _host.ErrorAsync("styles.delete", new { styleId = "corporate" })).GetProperty("code").GetString());
        Assert.Equal(1, (await _host.ResultAsync("styles.get", new { styleId })).GetProperty("usedByTemplates").GetInt32());
    }

    [Fact]
    public async Task PaperHtmlIsTheArticleOnlyAndThePreviewWorksWithoutARecording()
    {
        var settings = (await _host.ResultAsync("styles.get", new { styleId = "academic" })).GetProperty("settings");
        var sample = (await _host.ResultAsync("styles.sampleHtml", new { settings })).GetProperty("html").GetString()!;
        Assert.StartsWith("<article", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("<style", sample, StringComparison.OrdinalIgnoreCase);

        var preview = (await _host.ResultAsync("generation.previewHtml", new { recordingId = (string?)null, template = await _host.MeetingMinutesAsync(), styleId = "corporate" })).GetProperty("html").GetString()!;
        Assert.StartsWith("<article", preview, StringComparison.Ordinal);
        Assert.Contains(Memento.Documents.Render.SampleDocument.Minutes.Title.Replace(":", "", StringComparison.Ordinal)[..10], preview, StringComparison.Ordinal);
        Assert.Contains("Action items", preview, StringComparison.Ordinal);

        var modules = (await _host.ResultAsync("modules.list", new { })).GetProperty("modules");
        var actions = modules.EnumerateArray().Single(m => m.GetProperty("id").GetString() == "actionItems");
        Assert.Equal("table", actions.GetProperty("shape").GetString());
        Assert.True(actions.GetProperty("generated").GetBoolean());
        Assert.Contains("commitments", actions.GetProperty("groundingRule").GetString(), StringComparison.Ordinal);
        Assert.False(modules.EnumerateArray().Single(m => m.GetProperty("id").GetString() == "fullTranscript").GetProperty("generated").GetBoolean());
    }

    private static Document Document(string title) => new()
    {
        Title = title,
        Rows = [DocumentRow.Of(new ModuleBlock { Id = "m1", Type = ModuleIds.Summary, Title = "Summary", Blocks = [new ParagraphBlock { Runs = [Run.Plain(title)] }] })],
    };
}
