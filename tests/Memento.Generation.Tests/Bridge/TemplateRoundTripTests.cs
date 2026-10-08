using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Documents.Templates;
using Memento.Generation.Documents;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// A template saved from the Builder is written to the templates folder, listed after the built-ins, read back by id
/// (also by a new store over the same folder, as after a restart), and used by a generation; "Save as new template"
/// never takes the name of a template already in the library.
/// </summary>
public sealed class TemplateRoundTripTests : IDisposable
{
    private readonly M4Host _host = new();

    public void Dispose() => _host.Dispose();

    private string Folder => _host.Directory.File("templates");

    /// <summary>The template as the Builder sends it, with the first row removed and a new name.</summary>
    private async Task<JsonNode> ArrangedAsync(string templateId, string? name = null, string? id = null)
    {
        var template = JsonNode.Parse((await _host.ResultAsync("templates.get", new { templateId })).GetRawText())!;
        template["rows"]!.AsArray().RemoveAt(0);
        if (name is not null)
        {
            template["name"] = name;
        }

        if (id is not null)
        {
            template["id"] = id;
        }

        return template;
    }

    private async Task<List<(string Id, string Name, bool BuiltIn)>> ListAsync() =>
        (await _host.ResultAsync("templates.list", new { })).GetProperty("templates").EnumerateArray()
            .Select(t => (t.GetProperty("id").GetString()!, t.GetProperty("name").GetString()!, t.GetProperty("builtIn").GetBoolean()))
            .ToList();

    [Fact]
    public async Task ASavedTemplateIsWrittenListedAfterTheBuiltInsAndReadBack()
    {
        var saved = await _host.ResultAsync("templates.save", new { template = await ArrangedAsync("meeting-minutes", "Board minutes") });
        var id = saved.GetProperty("id").GetString()!;

        Assert.Equal("board-minutes", id);
        Assert.False(saved.GetProperty("builtIn").GetBoolean());
        Assert.True(File.Exists(Path.Combine(Folder, "board-minutes.json")));
        var list = await ListAsync();
        Assert.Equal(["meeting-minutes", "interview-notes", "lecture-summary", "dictation-cleanup", "board-minutes"], list.Select(t => t.Id));
        Assert.Equal(("board-minutes", "Board minutes", false), list[^1]);
        Assert.Single(_host.Events("templates.changed"));

        var got = await _host.ResultAsync("templates.get", new { templateId = id });
        var builtIn = await _host.ResultAsync("templates.get", new { templateId = "meeting-minutes" });
        Assert.Equal(builtIn.GetProperty("rows").GetArrayLength() - 1, got.GetProperty("rows").GetArrayLength());
        Assert.Equal("Board minutes", got.GetProperty("name").GetString());

        // After a restart: a new store over the same folder.
        var reopened = await new FileTemplateStore(Folder).GetAsync(id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal("Board minutes", reopened.Name);
        Assert.False(reopened.BuiltIn);
    }

    [Fact]
    public async Task SavingYourOwnTemplateAgainReplacesItAndABuiltInIsNeverSavedOver()
    {
        var first = await _host.ResultAsync("templates.save", new { template = await ArrangedAsync("meeting-minutes") });
        var id = first.GetProperty("id").GetString()!;
        Assert.Equal("Meeting minutes (copy)", first.GetProperty("name").GetString());

        var again = await ArrangedAsync(id);
        var second = await _host.ResultAsync("templates.save", new { template = again });

        Assert.Equal(id, second.GetProperty("id").GetString());
        Assert.Equal("Meeting minutes (copy)", second.GetProperty("name").GetString());
        Assert.Equal(first.GetProperty("rows").GetArrayLength() - 1, second.GetProperty("rows").GetArrayLength());
        Assert.Equal(5, (await ListAsync()).Count);
        Assert.Equal(["meeting-minutes-copy.json"], Directory.GetFiles(Folder, "*.json").Select(Path.GetFileName));
        var builtIn = await _host.ResultAsync("templates.get", new { templateId = "meeting-minutes" });
        Assert.True(builtIn.GetProperty("builtIn").GetBoolean());
        Assert.Equal(6, builtIn.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task SaveAsNewNeverTakesANameAlreadyInTheLibrary()
    {
        var copy = await _host.ResultAsync("templates.save", new { template = await ArrangedAsync("meeting-minutes") });
        var copyTwo = await _host.ResultAsync("templates.save", new { template = await ArrangedAsync("meeting-minutes") });
        var asNew = await _host.ResultAsync("templates.save", new { template = await ArrangedAsync(copy.GetProperty("id").GetString()!, id: string.Empty) });
        var renamed = await _host.ResultAsync("templates.save", new { template = await ArrangedAsync("interview-notes", "interview NOTES", string.Empty) });

        Assert.Equal(("meeting-minutes-copy", "Meeting minutes (copy)"), (copy.GetProperty("id").GetString(), copy.GetProperty("name").GetString()));
        Assert.Equal(("meeting-minutes-copy-2", "Meeting minutes (copy 2)"), (copyTwo.GetProperty("id").GetString(), copyTwo.GetProperty("name").GetString()));
        Assert.Equal("Meeting minutes (copy) (copy)", asNew.GetProperty("name").GetString());
        Assert.Equal("interview NOTES (copy)", renamed.GetProperty("name").GetString());
        Assert.Equal(4, Directory.GetFiles(Folder, "*.json").Length);
    }

    [Theory]
    [InlineData("Board minutes", new string[0], "Board minutes")]
    [InlineData("Board minutes", new[] { "board minutes" }, "Board minutes (copy)")]
    [InlineData("Board minutes", new[] { "Board minutes", "Board minutes (copy)", "Board minutes (copy 2)" }, "Board minutes (copy 3)")]
    public void UniqueNamesCountUp(string name, string[] taken, string expected)
    {
        Assert.Equal(expected, TemplateService.UniqueName(name, taken.ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AGenerationWithTheSavedTemplateRecordsIt()
    {
        _host.InstallLocalModel();
        var recordingId = await _host.CreateMeetingAsync();
        var saved = await _host.ResultAsync("templates.save", new { template = await ArrangedAsync("meeting-minutes", "Board minutes") });
        var template = await _host.ResultAsync("templates.get", new { templateId = saved.GetProperty("id").GetString() });

        var start = await _host.ResultAsync("generation.start", new { recordingId, template });
        var done = await _host.FinishedAsync(start.GetProperty("jobId").GetString()!);

        Assert.Equal("done", done.GetProperty("stage").GetString());
        var document = (await _host.ResultAsync("documents.get", new { recordingId, documentId = done.GetProperty("documentId").GetString() })).GetProperty("document");
        Assert.Equal("board-minutes", document.GetProperty("record").GetProperty("templateId").GetString());
        Assert.Equal("Board minutes", document.GetProperty("record").GetProperty("templateName").GetString());
    }
}
