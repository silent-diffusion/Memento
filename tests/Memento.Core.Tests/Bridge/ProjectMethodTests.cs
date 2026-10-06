using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Bridge;

/// <summary><c>project.*</c>, <c>annotations.*</c>, <c>library.*</c>, <c>dialog.*</c> through the router.</summary>
public sealed class ProjectMethodTests : IDisposable
{
    private static readonly string[] PeopleWithBlank = ["Avery Stone", " ", "Rowan Hale"];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string? ErrorCode(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Theory]
    [InlineData("20261006-100000-aaaaaa")]
    [InlineData("..\\..\\Windows")]
    [InlineData("../../etc")]
    [InlineData("C:\\Windows")]
    public async Task UnknownOrUnsafeIdsAreNotFound(string recordingId)
    {
        foreach (var method in new[] { "project.get", "project.deleteEstimate", "project.delete", "recovery.acknowledge" })
        {
            var response = await _host.CallAsync(method, Json(new { recordingId }));

            Assert.Equal(DomainErrorCodes.ProjectNotFound, ErrorCode(response));
            Assert.Equal(
                "This recording is no longer in the library; it may have been deleted. Nothing was changed. Go back to the Library to see what is there.",
                response.GetProperty("error").GetProperty("message").GetString());
        }

        Assert.Equal(DomainErrorCodes.ProjectNotFound, ErrorCode(await _host.CallAsync("project.rename", Json(new { recordingId, title = "x" }))));
        Assert.Equal(DomainErrorCodes.ProjectNotFound, ErrorCode(await _host.CallAsync("annotations.addTopic", Json(new { recordingId, topic = new { label = "x" } }))));
        Assert.False(Directory.Exists(Path.Combine(_host.Root, "Windows")));
    }

    [Fact]
    public async Task UpdateDetailsIsPartialAndReachesSearch()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);

        var first = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { participants = PeopleWithBlank, purpose = "Plan Q4" } }));
        var second = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { location = "Room 4" } }));

        var details = second.GetProperty("details");
        Assert.Equal(["Avery Stone", "Rowan Hale"], details.GetProperty("participants").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Plan Q4", details.GetProperty("purpose").GetString());
        Assert.Equal("Room 4", details.GetProperty("location").GetString());
        Assert.Equal("Kickoff", details.GetProperty("title").GetString());
        Assert.Equal(2, first.GetProperty("summary").GetProperty("participantCount").GetInt32());
        var found = await _host.ResultAsync("library.list", """{"query":"rowan"}""");
        Assert.Equal(recordingId, found.GetProperty("recordings")[0].GetProperty("id").GetString());
        Assert.Contains(_host.Sink.Payloads(BridgeEventNames.LibraryChanged), p => p.GetProperty("recordingIds")[0].GetString() == recordingId);
        var history = second.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("summary").GetString()).ToList();
        Assert.Equal(2, history.Count(h => h == "Details edited"));
    }

    [Fact]
    public async Task UpdateDetailsValidates()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { title = "" } }))));
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { notes = new string('x', 5000) } }))));
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { colour = "red" } }))));
    }

    [Fact]
    public async Task UpdateDetailsReplacesTheAgenda()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);

        var project = await _host.ResultAsync(
            "project.updateDetails",
            Json(new { recordingId, details = new { agenda = new { source = (string?)null, parsedLocally = false, items = new[] { new { id = "", text = " Budget ", covered = false, uncertain = false, uncertainReason = (string?)null } } } } }));

        var item = project.GetProperty("details").GetProperty("agenda").GetProperty("items")[0];
        Assert.Equal("Budget", item.GetProperty("text").GetString());
        Assert.StartsWith("a", item.GetProperty("id").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameChangesTheTitleEverywhere()
    {
        var recordingId = await _host.RecordAsync("Old name", 0.5);

        var project = await _host.ResultAsync("project.rename", Json(new { recordingId, title = "  New name  " }));
        var empty = await _host.CallAsync("project.rename", Json(new { recordingId, title = "   " }));

        Assert.Equal("New name", project.GetProperty("summary").GetProperty("title").GetString());
        Assert.Equal("New name", (await _host.ResultAsync("library.list")).GetProperty("recordings")[0].GetProperty("title").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(empty));
    }

    [Fact]
    public async Task DeleteEstimateNamesWhatGoesAndDeleteRemovesIt()
    {
        var recordingId = await _host.RecordAsync("Doomed", 1, TestRecordings.Mic, TestRecordings.SystemAudio);
        await _host.ResultAsync("annotations.addHighlight", Json(new { recordingId, highlight = new { atMs = 100 } }));
        var folder = _host.Store.GetProjectFolder(recordingId);

        var estimate = await _host.ResultAsync("project.deleteEstimate", Json(new { recordingId }));

        Assert.Equal("Doomed", estimate.GetProperty("title").GetString());
        Assert.Equal(_host.Store.GetSizeBytes(recordingId), estimate.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(["the recording", "its 2 tracks", "its highlight"], estimate.GetProperty("items").EnumerateArray().Select(e => e.GetString()));

        Assert.Equal("{}", (await _host.ResultAsync("project.delete", Json(new { recordingId }))).GetRawText());

        Assert.False(Directory.Exists(folder));
        Assert.Equal(0, (await _host.ResultAsync("library.list")).GetProperty("totalCount").GetInt32());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, ErrorCode(await _host.CallAsync("project.get", Json(new { recordingId }))));
    }

    [Fact]
    public async Task ChaptersAreAddedUpdatedRemovedAndKeptInOrder()
    {
        var recordingId = await _host.RecordAsync("Lecture", 2);

        var added = await _host.ResultAsync("annotations.addChapter", Json(new { recordingId, chapter = new { atMs = 1500, title = "Second" } }));
        await _host.ResultAsync("annotations.addChapter", Json(new { recordingId, chapter = new { atMs = 0, title = "First", origin = "local" } }));
        var id = added.GetProperty("chapters")[0].GetProperty("id").GetString()!;
        var updated = await _host.ResultAsync("annotations.updateChapter", Json(new { recordingId, chapter = new { id, title = "Middle" } }));

        var chapters = updated.GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(["First", "Middle"], chapters.Select(c => c.GetProperty("title").GetString()));
        Assert.Equal("local", chapters[0].GetProperty("origin").GetString());
        Assert.Equal(1500, chapters[1].GetProperty("atMs").GetInt64());
        Assert.Equal("user", chapters[1].GetProperty("origin").GetString());

        var removed = await _host.ResultAsync("annotations.removeChapter", Json(new { recordingId, chapterId = id }));
        Assert.Single(removed.GetProperty("chapters").EnumerateArray());
        Assert.Single((await _host.ResultAsync("project.get", Json(new { recordingId }))).GetProperty("chapters").EnumerateArray());
    }

    [Theory]
    [InlineData("annotations.addChapter", """{"chapter":{"title":"no time"}}""")]
    [InlineData("annotations.addChapter", """{"chapter":{"atMs":-5}}""")]
    [InlineData("annotations.addChapter", """{"chapter":{"atMs":5,"origin":"robot"}}""")]
    [InlineData("annotations.updateChapter", """{"chapter":{"title":"no id"}}""")]
    [InlineData("annotations.addHighlight", """{"highlight":{"note":"no time"}}""")]
    [InlineData("annotations.updateHighlight", """{"highlight":{"note":"no id"}}""")]
    [InlineData("annotations.addTopic", """{"topic":{"label":"  "}}""")]
    [InlineData("annotations.addTopic", """{"label":"not wrapped"}""")]
    [InlineData("annotations.removeTopic", """{"id":"wrong field"}""")]
    public async Task AnnotationErrorsAreSpecific(string method, string body)
    {
        var recordingId = await _host.RecordAsync("Target", 0.5);
        var parameters = body.Insert(1, $"\"recordingId\":\"{recordingId}\",");

        var response = await _host.CallAsync(method, parameters);

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(response));
        Assert.DoesNotContain("Something went wrong", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("annotations.updateChapter", """{"chapter":{"id":"c-missing","title":"x"}}""", "c-missing", "chapter")]
    [InlineData("annotations.removeChapter", """{"chapterId":"c-missing"}""", "c-missing", "chapter")]
    [InlineData("annotations.updateHighlight", """{"highlight":{"id":"h-missing"}}""", "h-missing", "highlight")]
    [InlineData("annotations.removeHighlight", """{"highlightId":"h-missing"}""", "h-missing", "highlight")]
    [InlineData("annotations.removeTopic", """{"topicId":"t-missing"}""", "t-missing", "topic")]
    public async Task UnknownAnnotationIdsAreNotFound(string method, string body, string id, string kind)
    {
        var recordingId = await _host.RecordAsync("Target", 0.5);
        var parameters = body.Insert(1, $"\"recordingId\":\"{recordingId}\",");

        var error = (await _host.CallAsync(method, parameters)).GetProperty("error");

        Assert.Equal(DomainErrorCodes.AnnotationsNotFound, error.GetProperty("code").GetString());
        Assert.Equal(
            $"That {kind} is not in this recording any more; it may have been removed. Nothing was changed. Reopen the recording to see its current {kind}s.",
            error.GetProperty("message").GetString());
        Assert.Equal(id, error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AddAssignsIdsAndIgnoresAnySentId()
    {
        var recordingId = await _host.RecordAsync("Ids", 1);

        var chapter = (await _host.ResultAsync("annotations.addChapter", Json(new { recordingId, chapter = new { id = "c-mine", atMs = 10, title = "Intro" } }))).GetProperty("chapters")[0];
        var highlight = (await _host.ResultAsync("annotations.addHighlight", Json(new { recordingId, highlight = new { id = "h-mine", atMs = 20 } }))).GetProperty("highlights")[0];
        var topic = (await _host.ResultAsync("annotations.addTopic", Json(new { recordingId, topic = new { id = "t-mine", label = "Budget" } }))).GetProperty("topics")[0];

        Assert.NotEqual("c-mine", chapter.GetProperty("id").GetString());
        Assert.NotEqual("h-mine", highlight.GetProperty("id").GetString());
        Assert.NotEqual("t-mine", topic.GetProperty("id").GetString());
        Assert.False(string.IsNullOrEmpty(chapter.GetProperty("id").GetString()));
        Assert.Equal(DomainErrorCodes.AnnotationsNotFound, ErrorCode(await _host.CallAsync("annotations.removeChapter", Json(new { recordingId, chapterId = "c-mine" }))));
        Assert.Equal(
            chapter.GetProperty("id").GetString(),
            (await _host.ResultAsync("project.get", Json(new { recordingId }))).GetProperty("chapters")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task HighlightsAndTopics()
    {
        var recordingId = await _host.RecordAsync("Review me", 1);

        var highlights = await _host.ResultAsync("annotations.addHighlight", Json(new { recordingId, highlight = new { atMs = 900, note = "Action item" } }));
        var id = highlights.GetProperty("highlights")[0].GetProperty("id").GetString()!;
        var updated = await _host.ResultAsync("annotations.updateHighlight", Json(new { recordingId, highlight = new { id, note = "Action item: send notes" } }));
        Assert.Equal("Action item: send notes", updated.GetProperty("highlights")[0].GetProperty("note").GetString());
        Assert.Empty((await _host.ResultAsync("annotations.removeHighlight", Json(new { recordingId, highlightId = id }))).GetProperty("highlights").EnumerateArray());

        await _host.ResultAsync("annotations.addTopic", Json(new { recordingId, topic = new { label = "Budget" } }));
        var topics = await _host.ResultAsync("annotations.addTopic", Json(new { recordingId, topic = new { label = "budget" } }));
        var topic = Assert.Single(topics.GetProperty("topics").EnumerateArray());
        Assert.Equal("Budget", topic.GetProperty("label").GetString());
        var removed = await _host.ResultAsync("annotations.removeTopic", Json(new { recordingId, topicId = topic.GetProperty("id").GetString() }));
        Assert.Empty(removed.GetProperty("topics").EnumerateArray());
    }

    [Fact]
    public async Task LibraryListFiltersSortsAndValidates()
    {
        await _host.RecordAsync("Short meeting", 0.5);
        var longer = await _host.RecordAsync("Long meeting", 2);
        await _host.ResultAsync("project.updateDetails", Json(new { recordingId = longer, details = new { type = "lecture" } }));

        var longest = await _host.ResultAsync("library.list", """{"sort":"longest"}""");
        var lectures = await _host.ResultAsync("library.list", """{"type":"lecture"}""");
        var none = await _host.ResultAsync("library.list", """{"query":"zebra"}""");

        Assert.Equal(["Long meeting", "Short meeting"], longest.GetProperty("recordings").EnumerateArray().Select(r => r.GetProperty("title").GetString()));
        Assert.Equal(2500, longest.GetProperty("totalDurationMs").GetInt64());
        Assert.Equal(1, lectures.GetProperty("totalCount").GetInt32());
        Assert.Equal(2000, lectures.GetProperty("totalDurationMs").GetInt64());
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("library.list", """{"sort":"random"}""")));
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("library.list", """{"filter":"x"}""")));
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("library.list", Json(new { query = new string('q', 300) }))));
    }

    [Fact]
    public async Task ProcessingCardShowsTheStoredStageDuringFinalize()
    {
        var (sessionId, recordingId) = await _host.StartAsync("Processing", TestRecordings.Mic);
        _host.Session.Advance(TimeSpan.FromSeconds(1));
        await _host.ResultAsync("recording.stop", Json(new { sessionId }));

        var active = await _host.Sink.WaitForAsync(BridgeEventNames.ProcessingProgress, p => p.GetProperty("stages")[0].GetProperty("state").GetString() == "active");
        await _host.Recordings.WhenIdleAsync();

        Assert.Equal(recordingId, active.GetProperty("recordingId").GetString());
        Assert.Equal("stored", active.GetProperty("stages")[0].GetProperty("stage").GetString());
        Assert.Equal("""{"current":null,"othersCount":0}""", (await _host.ResultAsync("library.processing")).GetRawText());
    }

    [Fact]
    public async Task ProcessingCardListsEveryStageWhileRowsHideFinishedStoredAndOptimize()
    {
        var recordingId = await _host.RecordAsync("Smaller", 1);
        var catalog = _host.Get<ProjectCatalog>();
        await catalog.UpdateAsync(
            recordingId,
            m => m with { Stages = [new StageStatus(StageNames.Stored, StageStates.Done, null, "Done"), new StageStatus(StageNames.Optimize, StageStates.Active, 40, "40%")] },
            CancellationToken.None);

        var current = (await _host.ResultAsync("library.processing")).GetProperty("current");
        var row = (await _host.ResultAsync("library.list")).GetProperty("recordings")[0];

        Assert.Equal(["stored", "optimize"], current.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("stage").GetString()));
        Assert.Equal(["optimize"], current.GetProperty("meta").GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("stage").GetString()));
        Assert.Equal(["optimize"], row.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("stage").GetString()));
        Assert.True(row.GetProperty("isProcessing").GetBoolean());

        await catalog.UpdateAsync(
            recordingId,
            m => m with { Stages = [new StageStatus(StageNames.Stored, StageStates.Done, null, "Done"), new StageStatus(StageNames.Optimize, StageStates.Done, null, "Done")] },
            CancellationToken.None);

        Assert.Equal("""{"current":null,"othersCount":0}""", (await _host.ResultAsync("library.processing")).GetRawText());
        Assert.Empty((await _host.ResultAsync("library.list")).GetProperty("recordings")[0].GetProperty("stages").EnumerateArray());
        Assert.Empty((await _host.ResultAsync("project.get", Json(new { recordingId }))).GetProperty("summary").GetProperty("stages").EnumerateArray());
    }

    [Fact]
    public async Task PickFolderPassesTitleAndOnlyAbsoluteInitialPaths()
    {
        _host.FolderPicker.Answer = null;

        var cancelled = await _host.ResultAsync("dialog.pickFolder", """{"title":"Move library","initialPath":"relative\\path"}""");
        await _host.ResultAsync("dialog.pickFolder", """{"title":"","initialPath":"D:\\Recordings"}""");

        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("path").ValueKind);
        Assert.Equal(("Move library", (string?)null), _host.FolderPicker.Calls[0]);
        Assert.Equal(("Choose a folder", "D:\\Recordings"), _host.FolderPicker.Calls[1]);
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(await _host.CallAsync("dialog.pickFolder", "{}")));
    }
}
