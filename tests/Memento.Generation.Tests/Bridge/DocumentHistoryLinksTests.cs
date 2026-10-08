using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Bridge;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>History lines that made a document open its stored copy (<c>history.links</c>, <c>documents.getVersion</c>).</summary>
public sealed class DocumentHistoryLinksTests : IDisposable
{
    private readonly M4Host _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task GenerationLinesOpenTheVersionTheyWroteAndAKeptVersionReadsWithItsPaper()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();
        var first = await _host.FinishedAsync((await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() })).GetProperty("jobId").GetString()!);
        var documentId = first.GetProperty("documentId").GetString()!;
        var template = JsonNode.Parse((await _host.MeetingMinutesAsync()).GetRawText())!;
        template["rows"]![0]!["modules"]![0]!["instructions"] = "Two sentences only.";
        await _host.FinishedAsync((await _host.ResultAsync("generation.start", new { recordingId = id, template, documentId })).GetProperty("jobId").GetString()!);

        var history = (await _host.ResultAsync("project.get", new { recordingId = id })).GetProperty("history").EnumerateArray().ToList();
        var links = (await _host.ResultAsync("history.links", new { recordingId = id })).GetProperty("links").EnumerateArray().ToList();

        var generated = links.Where(l => l.GetProperty("kind").GetString() == "document").ToList();
        Assert.Equal(2, generated.Count);
        Assert.All(generated, l => Assert.Equal("minutes", history[l.GetProperty("index").GetInt32()].GetProperty("stage").GetString()));
        Assert.All(generated, l => Assert.Equal(documentId, l.GetProperty("documentId").GetString()));
        Assert.Equal(["after it was generated", "after it was generated again"], generated.Select(l => l.GetProperty("after").GetString()));
        Assert.Equal("current", generated[1].GetProperty("versionId").GetString());
        var keptId = generated[0].GetProperty("versionId").GetString()!;
        var kept = (await _host.ResultAsync("documents.versions", new { recordingId = id, documentId })).GetProperty("versions")[1];
        Assert.Equal(kept.GetProperty("id").GetString(), keptId);

        var version = await _host.ResultAsync("documents.getVersion", new { recordingId = id, documentId, versionId = keptId });
        Assert.Equal(1, version.GetProperty("document").GetProperty("version").GetInt32());
        Assert.Contains("<article", version.GetProperty("html").GetString(), StringComparison.Ordinal);
        Assert.Equal(DomainErrorCodes.DocumentsVersionNotFound, (await _host.ErrorAsync("documents.getVersion", new { recordingId = id, documentId, versionId = "20200101T000000000Z" })).GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.String, version.GetProperty("html").ValueKind);
    }
}
