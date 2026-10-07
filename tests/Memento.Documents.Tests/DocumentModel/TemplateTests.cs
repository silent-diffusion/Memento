using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;
using Memento.Documents.Model.Storage;
using Memento.Documents.Templates;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

public sealed class TemplateTests
{
    private static readonly string[] StyleIds = ["corporate", "minimal", "academic"];

    [Fact]
    public void FourBuiltInTemplatesShipInOrder()
    {
        Assert.Equal(["Meeting minutes", "Interview notes", "Lecture summary", "Dictation clean-up"], BuiltInTemplates.All.Select(t => t.Name));
        Assert.All(BuiltInTemplates.All, t =>
        {
            Assert.True(t.BuiltIn);
            Assert.Empty(t.Validate(ModuleCatalog.Default));
            Assert.NotEmpty(t.RecordingTypes);
            Assert.Contains(t.DefaultStyleId, StyleIds);
        });
    }

    [Fact]
    public void MeetingMinutesIsTheSpecLayout()
    {
        var rows = BuiltInTemplates.MeetingMinutes.Rows.Select(r => r.Modules.Select(m => m.Type).ToArray()).ToArray();
        string[][] expected =
        [
            [ModuleIds.ExecutiveSummary],
            [ModuleIds.MeetingPurpose, ModuleIds.Participants],
            [ModuleIds.Agenda],
            [ModuleIds.Discussion],
            [ModuleIds.Decisions, ModuleIds.ActionItems],
            [ModuleIds.OpenQuestions, ModuleIds.NextMeeting],
        ];
        Assert.Equal(expected, rows);
        var t = BuiltInTemplates.MeetingMinutes;
        Assert.Equal("corporate", t.DefaultStyleId);
        Assert.True(t.Inputs.Transcript && t.Inputs.Participants && t.Inputs.Agenda && t.Inputs.Highlights);
        Assert.False(t.Inputs.ImportedDocuments);
        Assert.Equal("Table: action, owner, deadline. Only commitments actually made.", t.Modules().Single(m => m.Type == ModuleIds.ActionItems).Instructions);
        Assert.Equal(["meeting"], t.RecordingTypes);
        Assert.Equal([BuiltInTemplates.MeetingMinutes], BuiltInTemplates.ForRecordingType("meeting"));
        Assert.Contains(BuiltInTemplates.LectureSummary, BuiltInTemplates.ForRecordingType("presentation"));
    }

    [Fact]
    public void ValidateNamesBadRowsAndUnknownModules()
    {
        var t = BuiltInTemplates.MeetingMinutes;
        var broken = t with
        {
            Rows =
            [
                new TemplateRow(),
                new TemplateRow { Modules = [new TemplateModule { Id = "x", Type = "nope" }, new TemplateModule { Id = "x", Type = ModuleIds.Notes }] },
            ],
        };
        var problems = broken.Validate(ModuleCatalog.Default);
        Assert.Contains(problems, p => p.Contains("Row 1 holds 0 modules", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("\"nope\" is not in the catalog", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unique id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoreListsBuiltInsFirstThenUserTemplatesByName()
    {
        using var folder = new TempFolder();
        var store = new FileTemplateStore(folder.Path);
        await store.SaveAsync(Custom("zeta", "Zeta notes"), CancellationToken.None);
        await store.SaveAsync(Custom("alpha", "Alpha notes"), CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal(["meeting-minutes", "interview-notes", "lecture-summary", "dictation-cleanup", "alpha", "zeta"], list.Select(t => t.Id));
        Assert.False(list[4].BuiltIn);
        Assert.Null(await store.GetAsync("missing", CancellationToken.None));
        Assert.Null(await store.GetAsync("../escape", CancellationToken.None));
    }

    [Fact]
    public async Task SavingWritesAtomicallyAndKeepsUnknownFields()
    {
        using var folder = new TempFolder();
        var store = new FileTemplateStore(folder.Path);
        File.WriteAllText(folder.File("stale.json.tmp"), "{ left over from a crash");
        var node = JsonNode.Parse(JsonSerializer.Serialize(Custom("weekly", "Weekly sync"), TemplateJsonContext.Default.DocumentTemplate))!;
        node["futureOption"] = "kept";
        node["rows"]![0]!["modules"]![0]!["futureSetting"] = 7;
        var withExtras = JsonSerializer.Deserialize(node.ToJsonString(), TemplateJsonContext.Default.DocumentTemplate)!;

        await store.SaveAsync(withExtras, CancellationToken.None);

        Assert.True(File.Exists(folder.File("weekly.json")));
        Assert.False(File.Exists(folder.File("weekly.json.tmp")));
        var saved = JsonNode.Parse(File.ReadAllText(folder.File("weekly.json")))!;
        Assert.Equal("kept", (string)saved["futureOption"]!);
        Assert.Equal(7, (int)saved["rows"]![0]!["modules"]![0]!["futureSetting"]!);
        Assert.Equal(1, (int)saved["schemaVersion"]!);
        Assert.Equal(5, (await store.ListAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task ChangingABuiltInSavesACustomisedCopyThatResetRemoves()
    {
        using var folder = new TempFolder();
        var store = new FileTemplateStore(folder.Path);
        var changed = BuiltInTemplates.MeetingMinutes with { Name = "Minutes (ours)", DefaultStyleId = "minimal" };

        var saved = await store.SaveAsync(changed, CancellationToken.None);
        Assert.True(saved.BuiltIn);
        Assert.True(saved.Customized);
        var got = await store.GetAsync(BuiltInTemplates.MeetingMinutesId, CancellationToken.None);
        Assert.Equal("Minutes (ours)", got!.Name);
        Assert.True(got.Customized);
        Assert.Equal("Minutes (ours)", (await store.ListAsync(CancellationToken.None))[0].Name);

        var reset = await store.ResetBuiltInAsync(BuiltInTemplates.MeetingMinutesId, CancellationToken.None);
        Assert.Equal("Meeting minutes", reset.Name);
        Assert.False(reset.Customized);
        Assert.False(File.Exists(folder.File("meeting-minutes.json")));
        Assert.Equal("Meeting minutes", (await store.GetAsync(BuiltInTemplates.MeetingMinutesId, CancellationToken.None))!.Name);
    }

    [Fact]
    public async Task DuplicateCreatesAUniqueUserCopy()
    {
        using var folder = new TempFolder();
        var store = new FileTemplateStore(folder.Path);
        var first = await store.DuplicateAsync(BuiltInTemplates.MeetingMinutesId, null, CancellationToken.None);
        var second = await store.DuplicateAsync(BuiltInTemplates.MeetingMinutesId, null, CancellationToken.None);
        var named = await store.DuplicateAsync(first.Id, "Board minutes", CancellationToken.None);

        Assert.Equal("meeting-minutes-copy", first.Id);
        Assert.Equal("Meeting minutes (copy)", first.Name);
        Assert.Equal("meeting-minutes-copy-2", second.Id);
        Assert.Equal("board-minutes", named.Id);
        Assert.False(first.BuiltIn);
        Assert.Equal(BuiltInTemplates.MeetingMinutes.Rows.Count, first.Rows.Count);
        await Assert.ThrowsAsync<DocumentStoreException>(() => store.DuplicateAsync("missing", null, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRemovesUserTemplatesButNeverBuiltIns()
    {
        using var folder = new TempFolder();
        var store = new FileTemplateStore(folder.Path);
        await store.SaveAsync(Custom("weekly", "Weekly sync"), CancellationToken.None);
        await store.DeleteAsync("weekly", CancellationToken.None);
        Assert.Null(await store.GetAsync("weekly", CancellationToken.None));

        var builtIn = await Assert.ThrowsAsync<DocumentStoreException>(() => store.DeleteAsync(BuiltInTemplates.MeetingMinutesId, CancellationToken.None));
        Assert.Equal(StoreErrorCodes.BuiltInCannotBeDeleted, builtIn.Code);
        var missing = await Assert.ThrowsAsync<DocumentStoreException>(() => store.DeleteAsync("weekly", CancellationToken.None));
        Assert.Equal(StoreErrorCodes.NotFound, missing.Code);
        var notBuiltIn = await Assert.ThrowsAsync<DocumentStoreException>(() => store.ResetBuiltInAsync("weekly", CancellationToken.None));
        Assert.Equal(StoreErrorCodes.NotBuiltIn, notBuiltIn.Code);
    }

    [Fact]
    public async Task UnreadableFilesAreSkippedInTheListAndNamedOnGet()
    {
        using var folder = new TempFolder();
        var store = new FileTemplateStore(folder.Path);
        File.WriteAllText(folder.File("broken.json"), "{ not json");
        File.WriteAllText(folder.File("future.json"), "{ \"schemaVersion\": 9, \"id\": \"future\", \"name\": \"Future\" }");

        Assert.Equal(4, (await store.ListAsync(CancellationToken.None)).Count);
        var broken = await Assert.ThrowsAsync<DocumentStoreException>(() => store.GetAsync("broken", CancellationToken.None));
        Assert.Equal(StoreErrorCodes.Unreadable, broken.Code);
        Assert.Contains("broken.json", broken.Message, StringComparison.Ordinal);
        var future = await Assert.ThrowsAsync<DocumentStoreException>(() => store.GetAsync("future", CancellationToken.None));
        Assert.Contains("newer version", future.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidIdsAreRefused()
    {
        using var folder = new TempFolder();
        var store = new FileTemplateStore(folder.Path);
        var error = await Assert.ThrowsAsync<DocumentStoreException>(() => store.SaveAsync(Custom("../x", "Bad"), CancellationToken.None));
        Assert.Equal(StoreErrorCodes.InvalidId, error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));
        Assert.Equal("meeting-minutes-copy", StoreIds.Slug("Meeting minutes (copy)"));
        Assert.Equal("cafe-notes", StoreIds.Slug("Café notes"));
        Assert.Equal("item", StoreIds.Slug("!!!"));
    }

    private static DocumentTemplate Custom(string id, string name) => BuiltInTemplates.InterviewNotes with
    {
        Id = id,
        Name = name,
        BuiltIn = false,
        Rows = [new TemplateRow { Modules = [TemplateModule.FromCatalog(ModuleCatalog.Default.Get(ModuleIds.Summary), "m01") with { TextSize = TextSize.Larger }] }],
    };
}
