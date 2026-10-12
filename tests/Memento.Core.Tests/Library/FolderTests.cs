using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Library;

/// <summary>2.1: Library folders: the store, the bridge methods (strictly, BRIDGE.md), the filter and the v4 → v5 migration.</summary>
public sealed class FolderTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string Error(JsonElement response) => response.GetProperty("error").GetProperty("code").GetString()!;

    private Task<JsonElement> CreateAsync(string? parent, string name) =>
        _host.ResultAsync("folders.create", JsonSerializer.Serialize(new { parent, name }));

    private Task<JsonElement> MoveToAsync(string? folder, params string[] recordingIds) =>
        _host.ResultAsync("library.moveToFolder", JsonSerializer.Serialize(new { recordingIds, folder }));

    private async Task<string?> FolderOfAsync(string recordingId) => (await _host.Store.LoadAsync(recordingId, CancellationToken.None)).Folder;

    /// <summary>Every file of the recording's folder but project.json, with its size: what must not change when it is filed.</summary>
    private Dictionary<string, long> FilesOf(string recordingId)
    {
        var folder = _host.Store.GetProjectFolder(recordingId);
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(ProjectLayout.ManifestFile, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetRelativePath(folder, f), f => new FileInfo(f).Length);
    }

    [Theory]
    [InlineData(" Clients / Acme ", "Clients/Acme")]
    [InlineData("A", "A")]
    [InlineData("", null)]
    [InlineData("A//B", null)]
    [InlineData("A/../B", null)]
    [InlineData("A\\B", null)]
    [InlineData("1/2/3/4/5/6/7/8/9", null)]
    public void PathsAreNormalisedOrRefused(string path, string? expected)
    {
        Assert.Equal(expected is not null, FolderPaths.TryNormalize(path, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void PathsRebaseAndNest()
    {
        Assert.Equal("X/C", FolderPaths.Rebase("A/B/C", "A/B", "X"));
        Assert.Equal("C", FolderPaths.Rebase("A/B/C", "A/B", null));
        Assert.Null(FolderPaths.Rebase("A/B", "A/B", null));
        Assert.True(FolderPaths.IsWithin("a/b/c", "A/B"));
        Assert.False(FolderPaths.IsWithin("A/BC", "A/B"));
        Assert.Equal(["A", "A/B", "A/B/C"], FolderPaths.SelfAndAncestors("A/B/C"));
        Assert.False(FolderPaths.TryName(new string('x', 81), out _, out var problem));
        Assert.Equal("A folder name can be at most 80 characters; this one has 81.", problem);
    }

    [Fact]
    public void MigrationFromV4AddsTheRootAndDropsAnUnreadableFolder()
    {
        var plain = JsonNode.Parse("""{"schemaVersion":4,"id":"x","videoTracks":[]}""")!.AsObject();
        var migrated = ProjectManifestMigrator.Default.Migrate(plain);
        Assert.Equal((ProjectManifest.CurrentSchemaVersion, true), (migrated.Manifest["schemaVersion"]!.GetValue<int>(), migrated.Migrated));
        Assert.True(migrated.Manifest.ContainsKey("folder"));
        Assert.Null(migrated.Manifest["folder"]);

        Assert.Equal("A/B", FolderMigration.Upgrade(JsonNode.Parse("""{"folder":" A / B "}""")!.AsObject())["folder"]!.GetValue<string>());
        Assert.Null(FolderMigration.Upgrade(JsonNode.Parse("""{"folder":42}""")!.AsObject())["folder"]);
        Assert.Null(FolderMigration.Upgrade(JsonNode.Parse("""{"folder":"a/../b"}""")!.AsObject())["folder"]);
        Assert.Equal("""{"other":1,"folder":null}""", FolderMigration.Upgrade(JsonNode.Parse("""{"other":1}""")!.AsObject()).ToJsonString());
    }

    [Fact]
    public async Task AV4ProjectOnDiskReadsAtTheRootAndIsWrittenAsV5()
    {
        var recordingId = await _host.RecordAsync("Old one", 1);
        var path = Path.Combine(_host.Store.GetProjectFolder(recordingId), ProjectLayout.ManifestFile);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        json["schemaVersion"] = 4;
        json.Remove("folder");
        json.Remove("audioRemoved");
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Null(manifest.Folder);
        Assert.Equal(ProjectManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        await _host.Store.UpdateAsync(recordingId, m => m, CancellationToken.None);
        Assert.Equal(ProjectManifest.CurrentSchemaVersion, JsonNode.Parse(await File.ReadAllTextAsync(path))!["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task FoldersAreCreatedListedAndCountedExactly()
    {
        Assert.Equal("""{"folders":[],"rootCount":0}""", (await _host.ResultAsync("folders.list")).GetRawText());
        Assert.Equal("""{"path":"Clients"}""", (await CreateAsync(null, " Clients ")).GetRawText());
        Assert.Equal("""{"path":"Clients/Acme"}""", (await CreateAsync("clients", "Acme")).GetRawText());
        var one = await _host.RecordAsync("Kickoff", 1);
        var two = await _host.RecordAsync("Review", 1);
        await _host.RecordAsync("Loose", 1);
        Assert.Equal("""{"moved":1}""", (await MoveToAsync("Clients/Acme", one)).GetRawText());
        Assert.Equal("""{"moved":2}""", (await MoveToAsync("Clients", one, two)).GetRawText());
        Assert.Equal("""{"moved":0}""", (await MoveToAsync("clients", one)).GetRawText());

        Assert.Equal(
            """{"folders":[{"path":"Clients","name":"Clients","parent":null,"count":2,"totalCount":2},{"path":"Clients/Acme","name":"Acme","parent":"Clients","count":0,"totalCount":0}],"rootCount":1}""",
            (await _host.ResultAsync("folders.list")).GetRawText());
        await MoveToAsync("Clients/Acme", one);
        Assert.Equal(
            """{"folders":[{"path":"Clients","name":"Clients","parent":null,"count":1,"totalCount":2},{"path":"Clients/Acme","name":"Acme","parent":"Clients","count":1,"totalCount":1}],"rootCount":1}""",
            (await _host.ResultAsync("folders.list")).GetRawText());

        // folders.json keeps an empty folder through a rebuilt index.
        await CreateAsync(null, "Empty");
        await _host.Index.RebuildAsync(CancellationToken.None);
        var list = await _host.ResultAsync("folders.list");
        Assert.Contains(list.GetProperty("folders").EnumerateArray(), f => f.GetProperty("path").GetString() == "Empty");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_host.Library.Root, FolderStore.FileName)))!;
        Assert.Equal(1, saved["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task TheListFiltersByFolderWithSearchAndTypeStillApplied()
    {
        await CreateAsync(null, "Team");
        var standup = await _host.RecordAsync("Standup notes", 1);
        var retro = await _host.RecordAsync("Retro notes", 1);
        var root = await _host.RecordAsync("Standup at root", 1);
        await MoveToAsync("Team", standup, retro);

        static string[] Ids(JsonElement result) => result.GetProperty("recordings").EnumerateArray().Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { retro, standup }.Order(StringComparer.Ordinal), Ids(await _host.ResultAsync("library.list", """{"folder":"team"}""")));
        Assert.Equal([root], Ids(await _host.ResultAsync("library.list", """{"folder":null}""")));
        Assert.Equal(3, Ids(await _host.ResultAsync("library.list", "{}")).Length);
        Assert.Equal([standup], Ids(await _host.ResultAsync("library.list", """{"folder":"Team","query":"standup"}""")));
        Assert.Empty(Ids(await _host.ResultAsync("library.list", """{"folder":"Team","type":"lecture"}""")));
        var row = (await _host.ResultAsync("library.list", """{"folder":"Team","query":"standup"}""")).GetProperty("recordings")[0];
        Assert.Equal("Team", row.GetProperty("folder").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("audioRemoved").ValueKind);
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("library.list", """{"folder":"a/../b"}""")));
    }

    [Fact]
    public async Task FilingARecordingChangesOnlyItsManifest()
    {
        await CreateAsync(null, "Kept");
        var recordingId = await _host.RecordAsync("Unmoved files", 1);
        await _host.Processing.WhenIdleAsync();
        var before = FilesOf(recordingId);
        var folderBefore = _host.Store.GetProjectFolder(recordingId);

        await MoveToAsync("Kept", recordingId);
        await _host.ResultAsync("folders.rename", """{"path":"Kept","name":"Archive"}""");

        Assert.Equal("Archive", await FolderOfAsync(recordingId));
        Assert.Equal(folderBefore, _host.Store.GetProjectFolder(recordingId));
        Assert.Equal(before, FilesOf(recordingId));
        Assert.Equal(
            ["folders.json", "projects"],
            Directory.EnumerateFileSystemEntries(_host.Library.Root).Select(Path.GetFileName).OfType<string>().Where(n => !n.StartsWith("library.db", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RenameAndMoveCarryWhatTheFolderHolds()
    {
        await CreateAsync(null, "A");
        await CreateAsync("A", "B");
        await CreateAsync(null, "X");
        var deep = await _host.RecordAsync("Deep", 1);
        await MoveToAsync("A/B", deep);

        Assert.Equal("""{"path":"A2"}""", (await _host.ResultAsync("folders.rename", """{"path":"A","name":"A2"}""")).GetRawText());
        Assert.Equal("A2/B", await FolderOfAsync(deep));
        Assert.Equal("""{"path":"X/B"}""", (await _host.ResultAsync("folders.move", """{"path":"A2/B","parent":"X"}""")).GetRawText());
        Assert.Equal("X/B", await FolderOfAsync(deep));
        Assert.Equal("""{"path":"B"}""", (await _host.ResultAsync("folders.move", """{"path":"X/B","parent":null}""")).GetRawText());
        Assert.Equal("B", await FolderOfAsync(deep));
        Assert.Equal("""{"path":"b"}""", (await _host.ResultAsync("folders.rename", """{"path":"B","name":"b"}""")).GetRawText());

        var paths = (await _host.ResultAsync("folders.list")).GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("path").GetString()).ToList();
        Assert.Equal(["A2", "b", "X"], paths);
    }

    [Fact]
    public async Task DeletingAFolderMovesWhatItHeldUpAndDeletesNoRecording()
    {
        await CreateAsync(null, "Parent");
        await CreateAsync("Parent", "Gone");
        await CreateAsync("Parent/Gone", "Child");
        var direct = await _host.RecordAsync("In gone", 1);
        var nested = await _host.RecordAsync("In child", 1);
        await MoveToAsync("Parent/Gone", direct);
        await MoveToAsync("Parent/Gone/Child", nested);

        Assert.Equal("""{"recordingsMoved":2,"foldersMoved":1}""", (await _host.ResultAsync("folders.delete", """{"path":"Parent/Gone"}""")).GetRawText());

        Assert.Equal("Parent", await FolderOfAsync(direct));
        Assert.Equal("Parent/Child", await FolderOfAsync(nested));
        Assert.Equal(2, _host.Store.ListIds().Count);
        var paths = (await _host.ResultAsync("folders.list")).GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("path").GetString()).ToList();
        Assert.Equal(["Parent", "Parent/Child"], paths);

        Assert.Equal("""{"recordingsMoved":2,"foldersMoved":1}""", (await _host.ResultAsync("folders.delete", """{"path":"Parent"}""")).GetRawText());
        Assert.Null(await FolderOfAsync(direct));
        Assert.Equal("Child", await FolderOfAsync(nested));
    }

    [Fact]
    public async Task RefusalsHaveTheirCodesAndChangeNothing()
    {
        await CreateAsync(null, "Work");
        await CreateAsync("Work", "Inner");
        var recordingId = await _host.RecordAsync("Refused", 1);

        Assert.Equal("folder.nameTaken", Error(await _host.CallAsync("folders.create", """{"parent":null,"name":"work"}""")));
        Assert.Equal("folder.invalid", Error(await _host.CallAsync("folders.create", """{"parent":null,"name":"a/b"}""")));
        Assert.Equal("folder.invalid", Error(await _host.CallAsync("folders.create", """{"parent":null,"name":"   "}""")));
        Assert.Equal("folder.notFound", Error(await _host.CallAsync("folders.create", """{"parent":"Nope","name":"x"}""")));
        Assert.Equal("folder.invalid", Error(await _host.CallAsync("folders.move", """{"path":"Work","parent":"Work/Inner"}""")));
        Assert.Equal("folder.notFound", Error(await _host.CallAsync("folders.rename", """{"path":"Nope","name":"x"}""")));
        Assert.Equal("folder.notFound", Error(await _host.CallAsync("folders.delete", """{"path":"Nope"}""")));
        Assert.Equal("folder.notFound", Error(await _host.CallAsync("library.moveToFolder", JsonSerializer.Serialize(new { recordingIds = new[] { recordingId }, folder = "Nope" }))));
        Assert.Equal("project.notFound", Error(await _host.CallAsync("library.moveToFolder", """{"recordingIds":["20260101-000000-zzzzzz"],"folder":"Work"}""")));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("library.moveToFolder", """{"recordingIds":[],"folder":"Work"}""")));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("folders.create", """{"name":"x","extra":1}""")));
        Assert.Equal("bridge.invalidParams", Error(await _host.CallAsync("folders.rename", """{"path":"Work"}""")));

        await CreateAsync(null, "Inner");
        Assert.Equal("folder.nameTaken", Error(await _host.CallAsync("folders.rename", """{"path":"Inner","name":"WORK"}""")));
        Assert.Equal("folder.nameTaken", Error(await _host.CallAsync("folders.move", """{"path":"Work/Inner","parent":null}""")));
        Assert.Null(await FolderOfAsync(recordingId));
        var paths = (await _host.ResultAsync("folders.list")).GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("path").GetString()).ToList();
        Assert.Equal(["Inner", "Work", "Work/Inner"], paths);
    }

    [Fact]
    public async Task FolderChangesTellThePage()
    {
        _host.Sink.Clear();
        await CreateAsync(null, "Told");
        Assert.Contains(_host.Sink.Payloads("library.changed"), p => p.GetProperty("recordingIds").GetArrayLength() == 0);
    }
}
