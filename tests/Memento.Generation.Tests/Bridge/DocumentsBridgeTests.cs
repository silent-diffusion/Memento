using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Bridge;
using Memento.Documents.Model;
using Memento.Generation.Documents;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>Documents through the bridge: versions, regeneration, restore, the viewer's edits, rename, copy, delete and export.</summary>
public sealed class DocumentsBridgeTests : IDisposable
{
    private readonly M4Host _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task RegeneratingKeepsAVersionAndRestoringBringsItBack()
    {
        var (id, documentId) = await GenerateAsync();
        var first = await DocumentAsync(id, documentId);

        // Regenerate with a changed instruction.
        var template = JsonNode.Parse((await _host.MeetingMinutesAsync()).GetRawText())!;
        template["rows"]![0]!["modules"]![0]!["instructions"] = "Two sentences only.";
        var again = await _host.ResultAsync("generation.start", new { recordingId = id, template, documentId });
        Assert.Equal("done", (await _host.FinishedAsync(again.GetProperty("jobId").GetString()!)).GetProperty("stage").GetString());

        // The current content first (not restorable), then the kept version.
        var versions = (await _host.ResultAsync("documents.versions", new { recordingId = id, documentId })).GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);
        Assert.Equal(("current", "regenerated", 2), (versions[0].GetProperty("id").GetString(), versions[0].GetProperty("reason").GetString(), versions[0].GetProperty("version").GetInt32()));
        var kept = versions[1];
        Assert.Equal(("generated", 1), (kept.GetProperty("reason").GetString(), kept.GetProperty("version").GetInt32()));
        Assert.Equal(2, (await _host.ResultAsync("documents.list", new { recordingId = id })).GetProperty("documents")[0].GetProperty("versions").GetInt32());
        Assert.Equal(2, (await DocumentAsync(id, documentId)).GetProperty("version").GetInt32());
        Assert.Contains(_host.Providers.Local.Count == 2 ? "ok" : "no", "ok", StringComparison.Ordinal);

        var restored = await _host.ResultAsync("documents.restoreVersion", new { recordingId = id, documentId, versionId = kept.GetProperty("id").GetString() });
        Assert.Equal(first.GetProperty("rows").GetRawText(), restored.GetProperty("document").GetProperty("rows").GetRawText());
        var after = (await _host.ResultAsync("documents.versions", new { recordingId = id, documentId })).GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("reason").GetString()).ToList();
        Assert.Equal(["restored", "regenerated", "generated"], after);
        Assert.Contains(_host.Events("documents.changed"), e => e.GetProperty("reason").GetString() == "restored");
        Assert.Equal(DomainErrorCodes.DocumentsVersionNotFound, (await _host.ErrorAsync("documents.restoreVersion", new { recordingId = id, documentId, versionId = "20200101T000000000Z" })).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ViewerEditsRoundTripAndARunOfEditsIsOneVersion()
    {
        var (id, documentId) = await GenerateAsync();
        var html = (await _host.ResultAsync("documents.renderHtml", new { recordingId = id, documentId, mode = "view" })).GetProperty("html").GetString()!;
        var title = (await DocumentAsync(id, documentId)).GetProperty("title").GetString()!;

        var edited = html.Replace(title, "Ledgerly sync, corrected", StringComparison.Ordinal);
        var saved = await _host.ResultAsync("documents.saveEdit", new { recordingId = id, documentId, html = edited });
        Assert.Equal("Ledgerly sync, corrected", saved.GetProperty("document").GetProperty("title").GetString());
        Assert.Equal(2, saved.GetProperty("version").GetInt32());

        // Saving the same markup again changes nothing; a second edit in the run keeps no new version.
        Assert.Equal(2, (await _host.ResultAsync("documents.saveEdit", new { recordingId = id, documentId, html = edited })).GetProperty("version").GetInt32());
        await _host.ResultAsync("documents.saveEdit", new { recordingId = id, documentId, html = edited.Replace("Ledgerly sync, corrected", "Ledgerly sync", StringComparison.Ordinal) });
        var versions = (await _host.ResultAsync("documents.versions", new { recordingId = id, documentId })).GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(["edited", "generated"], versions.Select(v => v.GetProperty("reason").GetString()));

        // The rendered paper of the saved document reads back to the same blocks (the HtmlToBlocks round trip).
        var stored = await _host.Get<ProjectDocumentStore>().LoadAsync(id, documentId, CancellationToken.None);
        var again = (await _host.ResultAsync("documents.renderHtml", new { recordingId = id, documentId, mode = "view" })).GetProperty("html").GetString()!;
        var reparsed = Memento.Documents.Render.HtmlToBlocks.Apply(stored!, again);
        Assert.Equal(JsonSerializer.Serialize(stored!.Rows.SelectMany(r => r.Modules).Select(m => m.Blocks).ToList()), JsonSerializer.Serialize(reparsed.Rows.SelectMany(r => r.Modules).Select(m => m.Blocks).ToList()));
        Assert.True(stored.Modules().Any(m => m.Provenance.Edited) || stored.Title == "Ledgerly sync");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x>")]
    [InlineData("<p onclick=\"x()\">hi</p>")]
    public async Task MarkupTheViewerNeverProducesIsRefused(string fragment)
    {
        var (id, documentId) = await GenerateAsync();
        var html = (await _host.ResultAsync("documents.renderHtml", new { recordingId = id, documentId, mode = "view" })).GetProperty("html").GetString()!;
        var before = await DocumentAsync(id, documentId);

        var error = await _host.ErrorAsync("documents.saveEdit", new { recordingId = id, documentId, html = html.Replace("</article>", fragment + "</article>", StringComparison.Ordinal) });

        Assert.Equal(DomainErrorCodes.DocumentsUnsupportedEdit, error.GetProperty("code").GetString());
        Assert.Equal(before.GetRawText(), (await DocumentAsync(id, documentId)).GetRawText());
        Assert.Equal(DomainErrorCodes.DocumentsUnsupportedEdit, (await _host.ErrorAsync("documents.saveEdit", new { recordingId = id, documentId, html = "<p>just text</p>" })).GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRenameDuplicateAndDelete()
    {
        var id = await _host.CreateMeetingAsync();

        var created = await _host.ResultAsync("documents.create", new { recordingId = id, name = "My notes", styleId = "minimal" });
        var documentId = created.GetProperty("id").GetString()!;
        Assert.Equal("written", created.GetProperty("kind").GetString());
        Assert.Equal("minimal", created.GetProperty("styleId").GetString());
        var renamed = await _host.ResultAsync("documents.rename", new { recordingId = id, documentId, name = "Follow-ups" });
        Assert.Equal("Follow-ups", renamed.GetProperty("name").GetString());
        var copy = await _host.ResultAsync("documents.duplicate", new { recordingId = id, documentId });
        Assert.Equal("Follow-ups (copy)", copy.GetProperty("name").GetString());
        Assert.Equal(1, copy.GetProperty("version").GetInt32());
        Assert.Equal(2, (await _host.ResultAsync("documents.list", new { recordingId = id })).GetProperty("documents").GetArrayLength());

        await _host.ResultAsync("documents.delete", new { recordingId = id, documentId });
        Assert.Equal(DomainErrorCodes.DocumentsNotFound, (await _host.ErrorAsync("documents.get", new { recordingId = id, documentId })).GetProperty("code").GetString());
        Assert.Single((await _host.ResultAsync("documents.list", new { recordingId = id })).GetProperty("documents").EnumerateArray());
        Assert.Equal(["created", "edited", "created", "deleted"], _host.Events("documents.changed").Select(e => e.GetProperty("reason").GetString()));
        Assert.Equal(DomainErrorCodes.ProjectNotFound, (await _host.ErrorAsync("documents.list", new { recordingId = "20260101-000000-aaaaaa" })).GetProperty("code").GetString());
    }

    /// <summary>Security audit 2026-10-07, SA-09: a path from the page never replaces an existing file.</summary>
    [Fact]
    public async Task ExportNeverOverwritesAnExistingFile()
    {
        var (id, documentId) = await GenerateAsync();
        var folder = _host.Directory.File("theirs");
        Directory.CreateDirectory(folder);
        var theirs = Path.Combine(folder, "report.docx");
        await File.WriteAllTextAsync(theirs, "the user's own report");

        var result = await _host.ResultAsync("documents.export", new { recordingId = id, documentId, format = "docx", path = theirs });
        var unc = await _host.ErrorAsync("documents.export", new { recordingId = id, documentId, format = "docx", path = "\\\\203.0.113.9\\share\\report.docx" });

        Assert.Equal(Path.Combine(folder, "report (2).docx"), result.GetProperty("path").GetString());
        Assert.Equal("the user's own report", await File.ReadAllTextAsync(theirs));
        Assert.Equal(DomainErrorCodes.ExportDestinationUnwritable, unc.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportWritesWordMarkdownAndPdfWithTheirHashes()
    {
        var (id, documentId) = await GenerateAsync();
        var folder = _host.Directory.File("out");
        Directory.CreateDirectory(folder);

        foreach (var (format, extension) in new[] { ("docx", ".docx"), ("markdown", ".md"), ("pdf", ".pdf") })
        {
            var result = await _host.ResultAsync("documents.export", new { recordingId = id, documentId, format, path = Path.Combine(folder, "minutes") });
            var path = result.GetProperty("path").GetString()!;
            Assert.EndsWith(extension, path, StringComparison.Ordinal);
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(bytes.Length, result.GetProperty("bytes").GetInt64());
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), result.GetProperty("sha256").GetString());
        }

        var markdown = await File.ReadAllTextAsync(Path.Combine(folder, "minutes.md"));
        Assert.Contains("## Action items", markdown, StringComparison.Ordinal);
        var (html, options) = Assert.Single(_host.Pdf.Printed);
        Assert.Contains("@page", html, StringComparison.Ordinal);
        Assert.Equal(8.5, options.PageWidthInches);
        Assert.Equal(DomainErrorCodes.ExportDestinationUnwritable, (await _host.ErrorAsync("documents.export", new { recordingId = id, documentId, format = "docx", path = "relative.docx" })).GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, (await _host.ErrorAsync("documents.export", new { recordingId = id, documentId, format = "rtf" })).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheExportDialogsDocumentsRowWritesTheDocuments()
    {
        var (id, documentId) = await GenerateAsync();
        var folder = _host.Directory.File("export");
        Directory.CreateDirectory(folder);
        var selection = new
        {
            audioMixed = new { on = false, format = "flac" },
            tracks = new { on = false, format = "flac" },
            transcript = new { on = false, formats = Array.Empty<string>() },
            documents = new { on = true, documentIds = new[] { documentId }, format = "markdown" },
            details = new { on = false },
            attachments = new { on = false },
        };

        var estimate = await _host.ResultAsync("export.estimate", new { recordingId = id, selection });
        Assert.Contains(estimate.GetProperty("items").EnumerateArray(), i => i.GetProperty("component").GetString() == "documents" && i.GetProperty("name").GetString()!.EndsWith(" - Meeting minutes.md", StringComparison.Ordinal));

        var job = await _host.ResultAsync("export.run", new { recordingId = id, selection, destination = new { folder, createSubfolder = false }, remember = false });
        await ExportDoneAsync(job.GetProperty("jobId").GetString()!);
        var written = Directory.EnumerateFiles(folder, "*.md").Single();
        Assert.Contains("Meeting minutes", Path.GetFileName(written), StringComparison.Ordinal);
        Assert.Contains("## Decisions", await File.ReadAllTextAsync(written), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MakeTemplateKeepsTheDocumentsLayout()
    {
        var (id, documentId) = await GenerateAsync();

        var template = await _host.ResultAsync("documents.makeTemplate", new { recordingId = id, documentId, name = "Our minutes" });

        Assert.Equal("our-minutes", template.GetProperty("id").GetString());
        Assert.False(template.GetProperty("builtIn").GetBoolean());
        Assert.Equal(6, template.GetProperty("rows").GetArrayLength());
        Assert.Equal("local", template.GetProperty("providerId").GetString());
        Assert.Contains((await _host.ResultAsync("templates.list", new { })).GetProperty("templates").EnumerateArray(), t => t.GetProperty("id").GetString() == "our-minutes");
    }

    private async Task<(string RecordingId, string DocumentId)> GenerateAsync()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();
        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() });
        var done = await _host.FinishedAsync(start.GetProperty("jobId").GetString()!);
        Assert.Equal("done", done.GetProperty("stage").GetString());
        return (id, done.GetProperty("documentId").GetString()!);
    }

    private Task ExportDoneAsync(string jobId) =>
        Core.Tests.Fakes.TestRecordings.WaitUntilAsync(
            () => _host.Events("export.progress").Any(e => e.GetProperty("jobId").GetString() == jobId && e.GetProperty("state").GetString() is "done" or "failed"),
            "the export to finish");

    private async Task<JsonElement> DocumentAsync(string recordingId, string documentId) =>
        (await _host.ResultAsync("documents.get", new { recordingId, documentId })).GetProperty("document");
}
