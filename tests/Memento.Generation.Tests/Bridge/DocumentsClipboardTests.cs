using Memento.Core.Bridge;
using Memento.Core.Host;
using Memento.Core.Tests.Fakes;
using Memento.Generation.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Generation.Tests.Bridge;

/// <summary><c>documents.copy</c> against a fake clipboard: the export's Markdown as text and the print page as HTML.</summary>
public sealed class DocumentsClipboardTests : IDisposable
{
    private readonly FakeClipboard _clipboard = new();
    private readonly M4Host _host;

    public DocumentsClipboardTests() =>
        _host = new M4Host(services => services.Replace(ServiceDescriptor.Singleton<IClipboard>(_clipboard)));

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ADocumentIsCopiedAsMarkdownAndAsTheFormattedPage()
    {
        var (id, documentId) = await GenerateAsync();
        var folder = _host.Directory.File("out");
        Directory.CreateDirectory(folder);
        var exported = await _host.ResultAsync("documents.export", new { recordingId = id, documentId, format = "markdown", path = Path.Combine(folder, "minutes") });

        var result = await _host.ResultAsync("documents.copy", new { recordingId = id, documentId });

        var copy = Assert.Single(_clipboard.Copies);
        Assert.Equal(await File.ReadAllTextAsync(exported.GetProperty("path").GetString()!), copy.Text);
        Assert.Contains("## Action items", copy.Text, StringComparison.Ordinal);
        Assert.Equal($$"""{"characters":{{copy.Text.Length}},"formatted":true}""", result.GetRawText());
        var html = copy.Html!;
        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Contains("Action items", html, StringComparison.Ordinal);
        Assert.Contains("<!--StartFragment-->", ClipboardHtml.Wrap(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownDocumentOrABusyClipboardCopiesNothing()
    {
        var (id, documentId) = await GenerateAsync();

        var missing = await _host.ErrorAsync("documents.copy", new { recordingId = id, documentId = "nosuch" });
        _clipboard.Refuse = "Windows answered 0x800401D0.";
        var busy = await _host.ErrorAsync("documents.copy", new { recordingId = id, documentId });

        Assert.Equal(DomainErrorCodes.DocumentsNotFound, missing.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ClipboardUnavailable, busy.GetProperty("code").GetString());
        Assert.StartsWith("Windows did not let Memento use the clipboard, so “", busy.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", busy.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_clipboard.Copies);
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
}
