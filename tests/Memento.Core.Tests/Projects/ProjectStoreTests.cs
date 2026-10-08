using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Projects;

public sealed class ProjectStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1));

    private readonly TempDirectory _directory = new();

    public ProjectStoreTests()
    {
        Store = CreateStore(ProjectManifestMigrator.Default);
    }

    private ProjectStore Store { get; }

    public void Dispose() => _directory.Dispose();

    private ProjectStore CreateStore(ProjectManifestMigrator migrator) =>
        new(new FakeLibraryLocation(_directory.Path), TimeProvider.System, NullLogger<ProjectStore>.Instance, migrator);

    private Task<ProjectManifest> CreateAsync(string title = "Weekly sync") =>
        Store.CreateAsync(new ProjectCreateRequest(title, "meeting", Start, ProjectStates.Ready), CancellationToken.None);

    [Fact]
    public async Task CreateLaysOutTheProjectFolder()
    {
        var manifest = await CreateAsync();

        Assert.Matches("^20261006-100000-[0-9a-hjkmnp-tv-z]{6}$", manifest.Id);
        var folder = Store.GetProjectFolder(manifest.Id);
        Assert.Equal(Path.Combine(_directory.Path, "projects", manifest.Id), folder);
        Assert.True(File.Exists(Path.Combine(folder, "project.json")));
        Assert.True(File.Exists(Path.Combine(folder, "annotations.json")));
        Assert.True(Directory.Exists(Path.Combine(folder, "tracks")));
        Assert.True(Directory.Exists(Path.Combine(folder, "attachments")));
        Assert.True(Directory.Exists(Path.Combine(folder, "versions")));
        Assert.Equal([manifest.Id], Store.ListIds());
    }

    [Fact]
    public async Task ManifestRoundTripsWithSchemaVersionAndCamelCase()
    {
        var created = await CreateAsync();
        var saved = await Store.SaveAsync(
            created with
            {
                DurationMs = 61_000,
                Details = created.Details with { Participants = ["Avery", "Rowan"], Tags = ["q3"] },
                Tracks = [new ProjectTrack { Id = "mic", SourceId = "mic:x", SourceKind = "microphone", Name = "Mic", File = "tracks/mic.wav", SampleRate = 48_000, Channels = 1 }],
                Stages = [new StageStatus("stored", "done", null, "Done")],
            },
            CancellationToken.None);

        var loaded = await Store.LoadAsync(created.Id, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Store.GetProjectFolder(created.Id), "project.json")));

        Assert.Equal(ProjectManifest.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Weekly sync", document.RootElement.GetProperty("details").GetProperty("title").GetString());
        Assert.Equal("2026-10-06T10:00:00+01:00", document.RootElement.GetProperty("createdAt").GetString());
        Assert.Equal(61_000, loaded.DurationMs);
        Assert.Equal(["Avery", "Rowan"], loaded.Details.Participants);
        Assert.Equal("mic", Assert.Single(loaded.Tracks).Id);
        Assert.Equal(saved.ModifiedAt, loaded.ModifiedAt);
    }

    [Fact]
    public async Task EveryWriteBumpsModifiedAt()
    {
        var created = await CreateAsync();
        var first = await Store.SaveAsync(created, CancellationToken.None);
        var second = await Store.SaveAsync(first, CancellationToken.None);
        var third = await Store.UpdateAsync(created.Id, m => m with { DurationMs = 5 }, CancellationToken.None);

        Assert.True(first.ModifiedAt > created.ModifiedAt);
        Assert.True(second.ModifiedAt > first.ModifiedAt);
        Assert.True(third.ModifiedAt > second.ModifiedAt);
    }

    [Fact]
    public async Task UnknownFieldsSurviveARoundTrip()
    {
        var created = await CreateAsync();
        var path = Path.Combine(Store.GetProjectFolder(created.Id), "project.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        node["futureField"] = new JsonObject { ["nested"] = 3 };
        node["details"]!["mood"] = "calm";
        node["tracks"] = new JsonArray(new JsonObject
        {
            ["id"] = "mic", ["sourceId"] = "mic:x", ["sourceKind"] = "microphone", ["name"] = "Mic", ["file"] = "tracks/mic.wav", ["gainDb"] = -3,
        });
        await File.WriteAllTextAsync(path, node.ToJsonString());

        var loaded = await Store.LoadAsync(created.Id, CancellationToken.None);
        await Store.SaveAsync(loaded with { DurationMs = 1 }, CancellationToken.None);

        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(3, written["futureField"]!["nested"]!.GetValue<int>());
        Assert.Equal("calm", written["details"]!["mood"]!.GetValue<string>());
        Assert.Equal(-3, written["tracks"]![0]!["gainDb"]!.GetValue<int>());
        Assert.Equal(1, written["durationMs"]!.GetValue<long>());
    }

    [Fact]
    public async Task WritesAreAtomic()
    {
        var created = await CreateAsync();
        var folder = Store.GetProjectFolder(created.Id);
        var manifestPath = Path.Combine(folder, "project.json");

        // A crash mid-write leaves only a .tmp beside an intact manifest; it is ignored and replaced.
        await File.WriteAllTextAsync(manifestPath + ".tmp", "{ half written");
        var loaded = await Store.LoadAsync(created.Id, CancellationToken.None);
        Assert.Equal("Weekly sync", loaded.Details.Title);

        await Store.SaveAsync(loaded with { DurationMs = 9 }, CancellationToken.None);

        Assert.False(File.Exists(manifestPath + ".tmp"));
        Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal(9, (await Store.LoadAsync(created.Id, CancellationToken.None)).DurationMs);
    }

    [Fact]
    public async Task AFailedUpdateLeavesTheManifestAsItWas()
    {
        var created = await CreateAsync();
        var path = Path.Combine(Store.GetProjectFolder(created.Id), "project.json");
        var before = await File.ReadAllTextAsync(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store.UpdateAsync(created.Id, _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AManifestFromANewerMementoIsReadOnly()
    {
        var created = await CreateAsync();
        var path = Path.Combine(Store.GetProjectFolder(created.Id), "project.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        node["schemaVersion"] = 7;
        await File.WriteAllTextAsync(path, node.ToJsonString());

        var loaded = await Store.LoadAsync(created.Id, CancellationToken.None);

        Assert.Equal(7, loaded.SchemaVersion);
        var ex = await Assert.ThrowsAsync<ProjectSchemaException>(() => Store.SaveAsync(loaded, CancellationToken.None));
        Assert.Contains("newer version of Memento", ex.Message, StringComparison.Ordinal);
        Assert.Equal(7, JsonNode.Parse(await File.ReadAllTextAsync(path))!["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task TheMigrationHookUpgradesOlderManifestsOnRead()
    {
        var created = await CreateAsync();
        var path = Path.Combine(Store.GetProjectFolder(created.Id), "project.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        node.Remove("details");
        node["legacyTitle"] = "From v1";
        node["schemaVersion"] = 1;
        await File.WriteAllTextAsync(path, node.ToJsonString());

        // A hypothetical v2 moved "legacyTitle" into details.title.
        var migrator = new ProjectManifestMigrator(2, new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [1] = manifest =>
            {
                var title = manifest["legacyTitle"]!.GetValue<string>();
                manifest.Remove("legacyTitle");
                manifest["details"] = new JsonObject { ["title"] = title, ["type"] = "meeting" };
                return manifest;
            },
        });
        var store = CreateStore(migrator);

        var loaded = await store.LoadAsync(created.Id, CancellationToken.None);
        await store.SaveAsync(loaded, CancellationToken.None);

        Assert.Equal("From v1", loaded.Details.Title);
        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(2, written["schemaVersion"]!.GetValue<int>());
        Assert.False(written.ContainsKey("legacyTitle"));
    }

    [Fact]
    public void TheMigratorRefusesAMissingStepAndInvalidVersions()
    {
        var migrator = new ProjectManifestMigrator(3, new Dictionary<int, Func<JsonObject, JsonObject>> { [1] = m => m });

        Assert.Throws<ProjectSchemaException>(() => migrator.Migrate(new JsonObject { ["schemaVersion"] = 1 }));
        Assert.Throws<ProjectSchemaException>(() => migrator.Migrate(new JsonObject { ["schemaVersion"] = "one" }));
        Assert.Throws<ProjectSchemaException>(() => migrator.Migrate(new JsonObject { ["schemaVersion"] = 0 }));
        Assert.True(migrator.Migrate(new JsonObject { ["schemaVersion"] = 4 }).FromNewerVersion);
        Assert.Equal(1, ProjectManifestMigrator.Default.Migrate(new JsonObject()).FromVersion);
        Assert.True(ProjectManifestMigrator.Default.Migrate(new JsonObject { ["schemaVersion"] = 1 }).Migrated);
        Assert.False(ProjectManifestMigrator.Default.Migrate(new JsonObject { ["schemaVersion"] = ProjectManifest.CurrentSchemaVersion }).Migrated);
    }

    [Fact]
    public async Task AV1ManifestKeepsItsReadableAttachmentsAsTheTypedListAndDropsDamagedOnes()
    {
        var created = await CreateAsync();
        var path = Path.Combine(Store.GetProjectFolder(created.Id), "project.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        node["schemaVersion"] = 1;
        node.Remove("attachments");
        node["attachments"] = JsonNode.Parse("""
            [
              { "id": "a1", "name": "agenda.docx", "file": "attachments/agenda.docx", "sizeBytes": 12, "sha256": "ab", "addedAt": "2026-10-01T10:00:00+02:00", "kind": "agenda", "contentType": null, "note": "kept" },
              { "id": "a2", "name": "broken.pdf", "file": "attachments/broken.pdf", "sizeBytes": "twelve" },
              { "name": "no id.txt", "file": "attachments/no id.txt" },
              "not an object"
            ]
            """);
        await File.WriteAllTextAsync(path, node.ToJsonString());

        var loaded = await Store.LoadAsync(created.Id, CancellationToken.None);
        await Store.SaveAsync(loaded, CancellationToken.None);

        var attachment = Assert.Single(loaded.Attachments);
        Assert.Equal(("a1", "agenda", "attachments/agenda.docx", 12L), (attachment.Id, attachment.Kind, attachment.File, attachment.SizeBytes));
        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(ProjectManifest.CurrentSchemaVersion, written["schemaVersion"]!.GetValue<int>());
        Assert.Equal("kept", written["attachments"]![0]!["note"]!.GetValue<string>());
        Assert.Single(written["attachments"]!.AsArray());
    }

    [Fact]
    public void AV1AttachmentsFieldThatIsNotAListIsDropped()
    {
        var migrated = ProjectManifestMigrator.Default.Migrate(new JsonObject { ["schemaVersion"] = 1, ["attachments"] = "agenda.docx" });

        Assert.False(migrated.Manifest.ContainsKey("attachments"));
        Assert.Equal(ProjectManifest.CurrentSchemaVersion, migrated.Manifest["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task AV2ManifestGetsAnEmptyWhoSpokeAndIsWrittenAsV3()
    {
        var created = await CreateAsync();
        var path = Path.Combine(Store.GetProjectFolder(created.Id), "project.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        node["schemaVersion"] = 2;
        node["details"]!.AsObject().Remove("whoSpoke");
        await File.WriteAllTextAsync(path, node.ToJsonString());

        var loaded = await Store.LoadAsync(created.Id, CancellationToken.None);
        await Store.SaveAsync(loaded, CancellationToken.None);

        Assert.Null(loaded.Details.WhoSpoke.Count);
        Assert.Empty(loaded.Details.WhoSpoke.Names);
        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(3, written["schemaVersion"]!.GetValue<int>());
        Assert.Equal("""{"count":null,"names":[]}""", written["details"]!["whoSpoke"]!.ToJsonString());
    }

    [Theory]
    [InlineData("""{"count":3,"names":["Ana"," Ben ","ana",""]}""", """{"count":3,"names":["Ana","Ben"]}""")]
    [InlineData("""{"count":0,"names":"Ana"}""", """{"count":null,"names":[]}""")]
    [InlineData("""{"count":21,"names":[1,true,"Chris"]}""", """{"count":null,"names":["Chris"]}""")]
    [InlineData("""{"count":"two"}""", """{"count":null,"names":[]}""")]
    [InlineData("\"4 people\"", """{"count":null,"names":[]}""")]
    public void AWhoSpokeTheV3StepCannotReadKeepsWhatIsReadable(string value, string expected)
    {
        var manifest = new JsonObject { ["schemaVersion"] = 2, ["details"] = new JsonObject { ["title"] = "Sync", ["whoSpoke"] = JsonNode.Parse(value) } };

        var migrated = ProjectManifestMigrator.Default.Migrate(manifest);

        Assert.True(migrated.Migrated);
        Assert.Equal(expected, migrated.Manifest["details"]!["whoSpoke"]!.ToJsonString());
        Assert.Equal("Sync", migrated.Manifest["details"]!["title"]!.GetValue<string>());
    }

    [Fact]
    public void TwentyNamesAtMostSurviveTheV3Step()
    {
        var names = new JsonArray(Enumerable.Range(1, 25).Select(n => (JsonNode)JsonValue.Create($"Person {n}")!).ToArray());
        var manifest = new JsonObject { ["schemaVersion"] = 2, ["details"] = new JsonObject { ["whoSpoke"] = new JsonObject { ["count"] = 2, ["names"] = names } } };

        var whoSpoke = ProjectManifestMigrator.Default.Migrate(manifest).Manifest["details"]!["whoSpoke"]!;

        Assert.Equal(20, whoSpoke["names"]!.AsArray().Count);
        Assert.Equal(2, whoSpoke["count"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("..\\..\\Windows")]
    [InlineData("../evil")]
    [InlineData("20261006-100000-k3f9ab\\..\\..")]
    [InlineData("20261006-100000-K3F9AB")]
    [InlineData("20261006-100000-k3f9ai")]
    [InlineData("20261006-100000-k3f9ab\n")]
    [InlineData("")]
    public async Task IdsThatAreNotProjectIdsNeverTouchTheFileSystem(string id)
    {
        Assert.Throws<ProjectNotFoundException>(() => Store.GetProjectFolder(id));
        Assert.False(Store.Exists(id));
        await Assert.ThrowsAsync<ProjectNotFoundException>(() => Store.LoadAsync(id, CancellationToken.None));
        await Assert.ThrowsAsync<ProjectNotFoundException>(() => Store.DeleteAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task ListIdsIgnoresStrayFolders()
    {
        var created = await CreateAsync();
        Directory.CreateDirectory(Path.Combine(Store.ProjectsRoot, "not-a-project"));
        Directory.CreateDirectory(Path.Combine(Store.ProjectsRoot, "20261006-100000-aaaaaa"));

        Assert.Equal([created.Id], Store.ListIds());
    }

    [Fact]
    public async Task UnreadableManifestIsReportedAsNotFound()
    {
        var created = await CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(Store.GetProjectFolder(created.Id), "project.json"), "not json");

        await Assert.ThrowsAsync<ProjectNotFoundException>(() => Store.LoadAsync(created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task HistoryIsAppendOnlyJsonLines()
    {
        var created = await CreateAsync();
        await Store.AppendHistoryAsync(created.Id, new HistoryEntry(Start, "recorded", "started", "Recording started", null), CancellationToken.None);
        await Store.AppendHistoryAsync(created.Id, new HistoryEntry(Start.AddMinutes(5), "stored", "completed", "Stored 2 tracks", "FLAC"), CancellationToken.None);
        var path = Path.Combine(Store.GetProjectFolder(created.Id), "history.jsonl");

        // A line cut short by a crash is skipped; the lines around it are kept.
        await File.AppendAllTextAsync(path, "{\"schemaVersion\":1,\"at\":\"2026-10");
        await File.AppendAllTextAsync(path, "\n");
        await Store.AppendHistoryAsync(created.Id, new HistoryEntry(Start.AddMinutes(6), "edited", "info", "Renamed", null), CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(path);
        var history = await Store.ReadHistoryAsync(created.Id, CancellationToken.None);

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("{\"schemaVersion\":1,\"at\":\"2026-10-06T10:00:00+01:00\",\"stage\":\"recorded\"", lines[0], StringComparison.Ordinal);
        Assert.Equal(["Recording started", "Stored 2 tracks", "Renamed"], history.Select(h => h.Summary));
        Assert.Equal("FLAC", history[1].Detail);
    }

    [Fact]
    public async Task ReadersNeverBlockWriters()
    {
        // Regression: project.get reading history.jsonl while finalize appended to it failed the finalize.
        var created = await CreateAsync();
        var folder = Store.GetProjectFolder(created.Id);
        await Store.AppendHistoryAsync(created.Id, new HistoryEntry(Start, "recorded", "started", "Recording started", null), CancellationToken.None);

        var readTask = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await Store.ReadHistoryAsync(created.Id, CancellationToken.None);
                await Store.LoadAsync(created.Id, CancellationToken.None);
            }
        });
        for (var i = 0; i < 50; i++)
        {
            await Store.AppendHistoryAsync(created.Id, new HistoryEntry(Start, "edited", "info", $"Edit {i}", null), CancellationToken.None);
            await Store.UpdateAsync(created.Id, m => m with { DurationMs = i }, CancellationToken.None);
        }

        await readTask;
        Assert.Equal(51, (await Store.ReadHistoryAsync(created.Id, CancellationToken.None)).Count);
        Assert.Equal(49, (await Store.LoadAsync(created.Id, CancellationToken.None)).DurationMs);
        Assert.True(File.Exists(Path.Combine(folder, "project.json")));
    }

    [Fact]
    public async Task AnnotationsRoundTrip()
    {
        var created = await CreateAsync();

        await Store.UpdateAnnotationsAsync(
            created.Id,
            d => d with { Chapters = [new Chapter("c1", 1000, "Intro", "user")], Highlights = [new Highlight("h1", 500, "note", "user", null)] },
            CancellationToken.None);
        var loaded = await Store.LoadAnnotationsAsync(created.Id, CancellationToken.None);

        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal("Intro", Assert.Single(loaded.Chapters).Title);
        Assert.Equal(500, Assert.Single(loaded.Highlights).AtMs);
        Assert.Empty(loaded.Topics);
    }

    [Fact]
    public async Task DeleteRefusesWhileRecordingAndRemovesReadOnlyFilesOtherwise()
    {
        var created = await CreateAsync();
        var folder = Store.GetProjectFolder(created.Id);
        var track = Path.Combine(folder, "tracks", "mic.flac");
        await File.WriteAllBytesAsync(track, new byte[10]);
        File.SetAttributes(track, FileAttributes.ReadOnly);
        await Store.WriteRecordingStateAsync(new RecordingStateDocument { SessionId = "s", RecordingId = created.Id }, CancellationToken.None);

        await Assert.ThrowsAsync<ProjectBusyException>(() => Store.DeleteAsync(created.Id, CancellationToken.None));
        Assert.True(File.Exists(track));

        Store.DeleteRecordingState(created.Id);
        await Store.DeleteAsync(created.Id, CancellationToken.None);

        Assert.False(Directory.Exists(folder));
        Assert.Empty(Store.ListIds());
    }

    [Fact]
    public async Task SizeCountsEveryFile()
    {
        var created = await CreateAsync();
        var folder = Store.GetProjectFolder(created.Id);
        await File.WriteAllBytesAsync(Path.Combine(folder, "tracks", "mic.wav"), new byte[1000]);
        await File.WriteAllBytesAsync(Path.Combine(folder, "attachments", "agenda.docx"), new byte[234]);
        var json = new FileInfo(Path.Combine(folder, "project.json")).Length + new FileInfo(Path.Combine(folder, "annotations.json")).Length;

        Assert.Equal(1234 + json, Store.GetSizeBytes(created.Id));
    }

    [Fact]
    public async Task RecordingStateRoundTripsAndToleratesDamage()
    {
        var created = await CreateAsync();
        var state = new RecordingStateDocument
        {
            SessionId = "s1",
            RecordingId = created.Id,
            StartedAt = Start,
            CheckpointSeconds = 30,
            Tracks = [new RecordingStateTrack("mic", "mic:x", "microphone", "Mic", "tracks/mic.wav", 48_000, 1, 16, "pcm", 0, 960, null, null)],
        };

        await Store.WriteRecordingStateAsync(state, CancellationToken.None);
        var read = await Store.ReadRecordingStateAsync(created.Id, CancellationToken.None);

        Assert.True(Store.HasRecordingState(created.Id));
        Assert.Equal(960, Assert.Single(read!.Tracks).BytesAtCheckpoint);

        await File.WriteAllTextAsync(Path.Combine(Store.GetProjectFolder(created.Id), "recording.state.json"), "{");
        Assert.Null(await Store.ReadRecordingStateAsync(created.Id, CancellationToken.None));
    }
}
