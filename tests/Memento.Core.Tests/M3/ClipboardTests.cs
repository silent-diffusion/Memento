using System.Text;
using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Core.Tests.M3;

/// <summary>
/// <c>transcript.copy</c> (BRIDGE.md, "Clipboard and transcript text options") against a fake clipboard: the exact JSON,
/// the text the export formatter writes, a filtered view, every refusal, and the Windows HTML Format the documents use.
/// </summary>
public sealed class ClipboardTests : IDisposable
{
    private static readonly string[] FirstAndThird = ["s0003", "s0001"];
    private static readonly string[] OneGone = ["s0001", "s9999"];

    private readonly FakeClipboard _clipboard = new();
    private readonly M3Host _m3;

    public ClipboardTests() =>
        _m3 = new M3Host(configure: services => services.Replace(ServiceDescriptor.Singleton<IClipboard>(_clipboard)));

    public void Dispose() => _m3.Dispose();

    [Fact]
    public async Task CopyingTheTranscriptPutsTheExportTextOnTheClipboard()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);

        var result = await _m3.ResultAsync("transcript.copy", new { recordingId = id, format = "text" });

        var text = _clipboard.Current!.Text;
        Assert.Equal($$"""{"lines":3,"totalLines":3,"characters":{{text.Length}}}""", result.GetRawText());
        Assert.Null(_clipboard.Current.Html);
        Assert.StartsWith("Weekly sync\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("[0:00:00] Speaker 1: Welcome everyone to the planning meeting.\r\n[0:00:04] Speaker 2: Thanks. Let's review the budget first.\r\n[0:00:09] Speaker 1: The budget for the third quarter is approved.\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFilteredViewCopiesOnlyItsLinesWithTheChosenOptions()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);

        var result = await _m3.ResultAsync("transcript.copy", new
        {
            recordingId = id,
            format = "markdown",
            options = new { timestamps = false, speakers = false, layout = "lines" },
            segmentIds = FirstAndThird,
        });

        Assert.Equal(2, result.GetProperty("lines").GetInt32());
        Assert.Equal(3, result.GetProperty("totalLines").GetInt32());
        Assert.EndsWith("\r\n\r\nWelcome everyone to the planning meeting.\r\n\r\nThe budget for the third quarter is approved.\r\n", _clipboard.Current!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Thanks", _clipboard.Current.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Speaker", _clipboard.Current.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingOptionFieldsKeepTheirDefaults()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);

        await _m3.ResultAsync("transcript.copy", new { recordingId = id, format = "markdown", options = new { speakers = false } });

        // Markdown keeps a paragraph per speaker turn; the names are left out, the turns are not.
        Assert.Contains("\r\n\r\n[0:00:00] Welcome everyone to the planning meeting.\r\n\r\n[0:00:04] Thanks.", _clipboard.Current!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Speaker", _clipboard.Current.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("srt", null, "format: 'srt' cannot be copied")]
    [InlineData("text", "pages", "Transcript layout 'pages' is not available")]
    public async Task AnUnknownFormatOrLayoutIsRefused(string format, string? layout, string message)
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);

        var error = await _m3.ErrorAsync("transcript.copy", new { recordingId = id, format, options = layout is null ? null : new { layout } });

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.GetProperty("code").GetString());
        Assert.Contains(message, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_clipboard.Copies);
    }

    [Fact]
    public async Task NoLinesOrALineThatIsGoneCopiesNothing()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);

        var none = await _m3.ErrorAsync("transcript.copy", new { recordingId = id, format = "text", segmentIds = Array.Empty<string>() });
        var gone = await _m3.ErrorAsync("transcript.copy", new { recordingId = id, format = "text", segmentIds = OneGone });

        Assert.Equal(BridgeErrorCodes.InvalidParams, none.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.TranscriptSegmentNotFound, gone.GetProperty("code").GetString());
        Assert.Equal("s9999", gone.GetProperty("detail").GetString());
        Assert.Empty(_clipboard.Copies);
    }

    [Fact]
    public async Task ARecordingWithoutATranscriptOrAnUnknownOneIsRefused()
    {
        var id = await _m3.RecordAsync();

        var none = await _m3.ErrorAsync("transcript.copy", new { recordingId = id, format = "text" });
        var unknown = await _m3.ErrorAsync("transcript.copy", new { recordingId = "20990101-000000-nosuch", format = "text" });

        Assert.Equal(DomainErrorCodes.TranscriptNone, none.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, unknown.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AClipboardAnotherProgramHoldsAnswersClipboardUnavailableInWords()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);
        _clipboard.Refuse = "Windows answered 0x800401D0.";

        var error = await _m3.ErrorAsync("transcript.copy", new { recordingId = id, format = "text" });

        Assert.Equal(DomainErrorCodes.ClipboardUnavailable, error.GetProperty("code").GetString());
        Assert.Equal(
            "Windows did not let Memento use the clipboard, so the transcript was not copied. Nothing was changed. Try again in a moment; if another program keeps the clipboard open, close it first.",
            error.GetProperty("message").GetString());
        Assert.Equal("Windows answered 0x800401D0.", error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task WithoutAWindowThereIsNoClipboard()
    {
        var error = await Assert.ThrowsAsync<ClipboardUnavailableException>(() => new UnavailableClipboard().SetAsync(new ClipboardContent("x"), CancellationToken.None));

        Assert.Contains("no window", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownFieldsAreRefusedLikeEveryOtherMethod()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);

        var error = await _m3.ErrorAsync("transcript.copy", new { recordingId = id, format = "text", html = true });

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.GetProperty("code").GetString());
    }

    [Fact]
    public void TheHtmlFormatHeaderPointsAtThePageAndTheFragmentInUtf8Bytes()
    {
        const string page = "<!doctype html>\n<html><head><style>p{color:navy}</style></head><body class=\"paper\">\n<p>Café — naïve “quotes”</p>\n</body></html>\n";

        var wrapped = ClipboardHtml.Wrap(page);

        var bytes = Encoding.UTF8.GetBytes(wrapped);
        int Offset(string name) => int.Parse(wrapped.Split("\r\n").Single(l => l.StartsWith(name + ":", StringComparison.Ordinal))[(name.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("Version:0.9\r\nStartHTML:0000000", wrapped, StringComparison.Ordinal);
        Assert.Equal("<!doctype html>", Encoding.UTF8.GetString(bytes, Offset("StartHTML"), 15));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
        Assert.Equal("\n<p>Café — naïve “quotes”</p>\n", Encoding.UTF8.GetString(bytes, Offset("StartFragment"), Offset("EndFragment") - Offset("StartFragment")));
        Assert.Contains("<body class=\"paper\"><!--StartFragment-->", wrapped, StringComparison.Ordinal);
        Assert.Contains("<!--EndFragment--></body>", wrapped, StringComparison.Ordinal);
    }

    [Fact]
    public void AFragmentWithoutABodyGetsAMinimalPage()
    {
        var wrapped = ClipboardHtml.Wrap("<h1>Minutes</h1>");

        var bytes = Encoding.UTF8.GetBytes(wrapped);
        var start = int.Parse(wrapped.Split("\r\n")[3]["StartFragment:".Length..], System.Globalization.CultureInfo.InvariantCulture);
        var end = int.Parse(wrapped.Split("\r\n")[4]["EndFragment:".Length..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("<h1>Minutes</h1>", Encoding.UTF8.GetString(bytes, start, end - start));
        Assert.Contains("<meta charset=\"utf-8\"></head><body><!--StartFragment-->", wrapped, StringComparison.Ordinal);
    }

    [Fact]
    public void TheParamsAndResultRoundTripWithTheirFieldNames()
    {
        var parameters = JsonSerializer.Deserialize(
            """{"recordingId":"r1","format":"markdown","options":{"timestamps":false,"speakers":true,"layout":"turns"},"segmentIds":["s1"]}""",
            BridgeJsonContext.Default.TranscriptCopyParams)!;

        Assert.Equal(new TranscriptTextOptions { Timestamps = false, Speakers = true, Layout = "turns" }, parameters.Options);
        Assert.Equal(["s1"], parameters.SegmentIds);
        Assert.Equal("""{"lines":1,"totalLines":3,"characters":40}""", JsonSerializer.Serialize(new TranscriptCopyResult(1, 3, 40), BridgeJsonContext.Default.TranscriptCopyResult));
    }
}
