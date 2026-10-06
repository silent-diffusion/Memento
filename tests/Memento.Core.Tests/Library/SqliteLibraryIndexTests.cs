using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Library;

public sealed class SqliteLibraryIndexTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeLibraryLocation _library;
    private readonly ProjectStore _store;

    public SqliteLibraryIndexTests()
    {
        _library = new FakeLibraryLocation(_directory.Path);
        _store = new ProjectStore(_library, TimeProvider.System, NullLogger<ProjectStore>.Instance);
    }

    public void Dispose() => _directory.Dispose();

    private SqliteLibraryIndex CreateIndex() =>
        new(_library, _store, TimeProvider.System, NullLogger<SqliteLibraryIndex>.Instance);

    private async Task<ProjectManifest> AddAsync(string title, string type, int dayOffset, long durationMs, params string[] people)
    {
        var created = await _store.CreateAsync(
            new ProjectCreateRequest(title, type, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(1)).AddDays(dayOffset), ProjectStates.Ready),
            CancellationToken.None);
        return await _store.SaveAsync(created with { DurationMs = durationMs, Details = created.Details with { Participants = people } }, CancellationToken.None);
    }

    private async Task<SqliteLibraryIndex> SeededAsync()
    {
        await AddAsync("Q3 planning sync", "meeting", 0, 3_600_000, "Avery Stone", "Rowan Hale");
        await AddAsync("Interview with a designer", "interview", 1, 1_800_000, "Rowan Hale");
        await AddAsync("Thermodynamics lecture", "lecture", 2, 5_400_000);
        await AddAsync("Budget review", "meeting", 3, 600_000, "Édith Marlow");
        var index = CreateIndex();
        await index.InitializeAsync(CancellationToken.None);
        return index;
    }

    private static List<string> Titles(LibraryQueryResult result) => result.Recordings.Select(r => r.Title).ToList();

    [Fact]
    public async Task ANewIndexIsBuiltFromTheProjectFolders()
    {
        using var index = await SeededAsync();

        var all = await index.QueryAsync(LibraryQuery.All, CancellationToken.None);

        Assert.Equal(4, all.TotalCount);
        Assert.Equal(11_400_000, all.TotalDurationMs);
        Assert.Equal(["Budget review", "Thermodynamics lecture", "Interview with a designer", "Q3 planning sync"], Titles(all));
        Assert.True(File.Exists(Path.Combine(_directory.Path, "library.db")));
    }

    [Fact]
    public async Task SummariesCarryPeopleSizeAndState()
    {
        using var index = await SeededAsync();

        var sync = (await index.QueryAsync(new LibraryQuery("planning", null, LibrarySort.Newest), CancellationToken.None)).Recordings.Single();

        Assert.Equal(["Avery Stone", "Rowan Hale"], sync.People);
        Assert.Equal(2, sync.ParticipantCount);
        Assert.Equal("ready", sync.State);
        Assert.Equal(_store.GetSizeBytes(sync.Id), sync.SizeBytes);
        Assert.True(sync.SizeBytes > 0);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(1)), sync.CreatedAt);
        Assert.Equal(TimeSpan.FromHours(1), sync.CreatedAt.Offset);
    }

    [Theory]
    [InlineData(LibrarySort.Oldest, "Q3 planning sync|Interview with a designer|Thermodynamics lecture|Budget review")]
    [InlineData(LibrarySort.Longest, "Thermodynamics lecture|Q3 planning sync|Interview with a designer|Budget review")]
    [InlineData(LibrarySort.Title, "Budget review|Interview with a designer|Q3 planning sync|Thermodynamics lecture")]
    public async Task Sorts(string sort, string expected)
    {
        using var index = await SeededAsync();

        Assert.Equal(expected, string.Join("|", Titles(await index.QueryAsync(new LibraryQuery(null, null, sort), CancellationToken.None))));
    }

    [Fact]
    public async Task FiltersByTypeAndTotalsReflectTheFilter()
    {
        using var index = await SeededAsync();

        var meetings = await index.QueryAsync(new LibraryQuery(null, "meeting", LibrarySort.Newest), CancellationToken.None);
        var all = await index.QueryAsync(new LibraryQuery(null, "all", LibrarySort.Newest), CancellationToken.None);

        Assert.Equal(["Budget review", "Q3 planning sync"], Titles(meetings));
        Assert.Equal(2, meetings.TotalCount);
        Assert.Equal(4_200_000, meetings.TotalDurationMs);
        Assert.Equal(4, all.TotalCount);
    }

    [Theory]
    [InlineData("plan", "Q3 planning sync")]
    [InlineData("rowan", "Interview with a designer|Q3 planning sync")]
    [InlineData("ROWAN sync", "Q3 planning sync")]
    [InlineData("edith", "Budget review")]
    [InlineData("thermo lect", "Thermodynamics lecture")]
    [InlineData("nothing-like-this", "")]
    [InlineData("\"budget\" OR *", "")]
    [InlineData("  ", "Budget review|Thermodynamics lecture|Interview with a designer|Q3 planning sync")]
    [InlineData("- * ()", "Budget review|Thermodynamics lecture|Interview with a designer|Q3 planning sync")]
    public async Task SearchesTitlesAndPeopleByPrefix(string query, string expected)
    {
        using var index = await SeededAsync();

        Assert.Equal(expected, string.Join("|", Titles(await index.QueryAsync(new LibraryQuery(query, null, LibrarySort.Newest), CancellationToken.None))));
    }

    [Fact]
    public async Task SearchCombinesWithTheTypeFilter()
    {
        using var index = await SeededAsync();

        var result = await index.QueryAsync(new LibraryQuery("rowan", "interview", LibrarySort.Newest), CancellationToken.None);

        Assert.Equal(["Interview with a designer"], Titles(result));
        Assert.Equal(1_800_000, result.TotalDurationMs);
    }

    [Fact]
    public async Task UpsertReplacesAndRemoveDeletes()
    {
        using var index = await SeededAsync();
        var target = (await index.QueryAsync(new LibraryQuery("budget", null, LibrarySort.Newest), CancellationToken.None)).Recordings.Single();
        var manifest = await _store.UpdateAsync(target.Id, m => m with { Details = m.Details with { Title = "Budget review (final)", Participants = [] } }, CancellationToken.None);

        await index.UpsertAsync(manifest, CancellationToken.None);
        var renamed = await index.QueryAsync(new LibraryQuery("final", null, LibrarySort.Newest), CancellationToken.None);
        Assert.Equal(["Budget review (final)"], Titles(renamed));
        Assert.Empty((await index.QueryAsync(new LibraryQuery("edith", null, LibrarySort.Newest), CancellationToken.None)).Recordings);

        await index.RemoveAsync(target.Id, CancellationToken.None);
        Assert.Equal(3, (await index.QueryAsync(LibraryQuery.All, CancellationToken.None)).TotalCount);
    }

    [Fact]
    public async Task ProcessingListsActiveStagesWithStoredIncluded()
    {
        using var index = await SeededAsync();
        var target = (await index.QueryAsync(new LibraryQuery("lecture", null, LibrarySort.Newest), CancellationToken.None)).Recordings.Single();
        var manifest = await _store.UpdateAsync(target.Id, m => m with { Stages = [new StageStatus("stored", "active", 40, "40%")] }, CancellationToken.None);
        await index.UpsertAsync(manifest, CancellationToken.None);

        var processing = Assert.Single(await index.ListProcessingAsync(CancellationToken.None));
        Assert.Equal(target.Id, processing.Summary.Id);
        Assert.True(processing.Summary.IsProcessing);
        Assert.Equal("stored", Assert.Single(processing.Stages).Stage);

        var done = await _store.UpdateAsync(target.Id, m => m with { Stages = [new StageStatus("stored", "done", null, "Done")] }, CancellationToken.None);
        await index.UpsertAsync(done, CancellationToken.None);
        Assert.Empty(await index.ListProcessingAsync(CancellationToken.None));

        // A finished "stored" stage is not a pill: the row reads "Audio only".
        var row = (await index.QueryAsync(new LibraryQuery("lecture", null, LibrarySort.Newest), CancellationToken.None)).Recordings.Single();
        Assert.Empty(row.Stages);
        Assert.False(row.IsProcessing);
    }

    [Fact]
    public async Task UsesWalJournalMode()
    {
        using var index = await SeededAsync();
        await using var connection = new SqliteConnection($"Data Source={index.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";

        Assert.Equal("wal", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ACorruptDatabaseIsSetAsideAndRebuilt()
    {
        await AddAsync("Survivor", "meeting", 0, 1000);
        var dbPath = Path.Combine(_directory.Path, "library.db");
        await File.WriteAllBytesAsync(dbPath, Enumerable.Range(0, 8192).Select(i => (byte)(i * 7)).ToArray());

        using var index = CreateIndex();
        await index.InitializeAsync(CancellationToken.None);

        var all = await index.QueryAsync(LibraryQuery.All, CancellationToken.None);
        Assert.Equal(["Survivor"], Titles(all));
        Assert.Single(Directory.EnumerateFiles(_directory.Path, "library.db.corrupt-*"));
    }

    [Fact]
    public async Task RebuildReadsTheFoldersAgain()
    {
        using var index = await SeededAsync();
        await AddAsync("Added behind the index's back", "general", 9, 10);

        Assert.Equal(4, (await index.QueryAsync(LibraryQuery.All, CancellationToken.None)).TotalCount);
        Assert.Equal(5, await index.RebuildAsync(CancellationToken.None));
        Assert.Equal(5, (await index.QueryAsync(LibraryQuery.All, CancellationToken.None)).TotalCount);
    }

    [Fact]
    public async Task AnUnreadableProjectIsLeftOutOfARebuild()
    {
        var good = await AddAsync("Good", "meeting", 0, 1);
        var bad = await AddAsync("Bad", "meeting", 1, 1);
        await File.WriteAllTextAsync(Path.Combine(_store.GetProjectFolder(bad.Id), "project.json"), "garbage");

        using var index = CreateIndex();
        await index.InitializeAsync(CancellationToken.None);

        Assert.Equal([good.Id], (await index.QueryAsync(LibraryQuery.All, CancellationToken.None)).Recordings.Select(r => r.Id));
    }

    [Fact]
    public async Task ListsIdsByState()
    {
        var recovered = await AddAsync("Recovered one", "meeting", 0, 1);
        await _store.UpdateAsync(recovered.Id, m => m with { State = ProjectStates.Recovered }, CancellationToken.None);
        await AddAsync("Other", "meeting", 1, 1);
        using var index = CreateIndex();

        Assert.Equal([recovered.Id], await index.ListIdsByStateAsync(ProjectStates.Recovered, CancellationToken.None));
    }

    [Fact]
    public void FtsQueriesQuoteEveryWord()
    {
        Assert.Equal("\"q3\"* AND \"plan\"*", FtsQuery.Build("q3 plan"));
        Assert.Equal("\"budgetOR\"*", FtsQuery.Build("\"budget\"OR"));
        Assert.Null(FtsQuery.Build(" - * "));
        Assert.Null(FtsQuery.Build(null));
    }
}
