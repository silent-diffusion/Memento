using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.AI;
using Memento.Core.Bridge;
using Memento.Core.Host;
using Memento.Core.Secrets;
using Memento.Core.Tests.Fakes;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// "Preview what will be sent" as a sheet (2.0): per pass the exact request the provider will receive (the same text a
/// generation then sends), the inputs ticked with their sizes, what is held back, the provider and model and "Ask
/// before every send"; and the copy of it through the host clipboard. Nothing is sent or constructed.
/// </summary>
public sealed class PreviewSheetTests : IDisposable
{
    private readonly FakeClipboard _clipboard = new();
    private readonly M4Host _host;

    public PreviewSheetTests() =>
        _host = new M4Host(services => services.Replace(ServiceDescriptor.Singleton<IClipboard>(_clipboard)));

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task EveryMapRequestIsShownExactlyAsTheLocalModelThenReadsIt()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();
        var template = await _host.MeetingMinutesAsync();

        var preview = await _host.ResultAsync("generation.preview", new { recordingId = id, template });

        var provider = preview.GetProperty("provider");
        Assert.Equal("local", provider.GetProperty("id").GetString());
        Assert.Equal("local", provider.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrEmpty(provider.GetProperty("modelLabel").GetString()));
        Assert.True(preview.GetProperty("askBeforeSend").GetBoolean());
        Assert.Equal(0, _host.Providers.LocalCreated + _host.Providers.CloudCreated);

        var passes = preview.GetProperty("passes").EnumerateArray().ToList();
        var map = passes.Where(p => p.GetProperty("step").GetString() == "map").ToList();
        Assert.Equal(passes.Select((_, i) => $"p{i + 1}"), passes.Select(p => p.GetProperty("id").GetString()));
        Assert.All(map, p => Assert.True(p.GetProperty("exact").GetBoolean()));
        Assert.StartsWith("Decisions and action items · segment 1 of ", map.Select(p => p.GetProperty("title").GetString()).First(t => t!.StartsWith("Decisions", StringComparison.Ordinal)), StringComparison.Ordinal);
        var verify = passes[^1];
        Assert.Equal("verify", verify.GetProperty("step").GetString());
        Assert.False(verify.GetProperty("exact").GetBoolean());
        Assert.All(passes, p =>
        {
            var system = p.GetProperty("system").GetString()!;
            var user = p.GetProperty("user").GetString()!;
            Assert.Equal(system.Length + user.Length, p.GetProperty("characters").GetInt64());
            Assert.True(p.GetProperty("bytes").GetInt64() >= p.GetProperty("characters").GetInt64());
            Assert.True(p.GetProperty("tokens").GetInt32() > 0);
        });

        // The generation then sends exactly these map requests, in this order.
        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template });
        Assert.Equal("done", (await _host.FinishedAsync(start.GetProperty("jobId").GetString()!)).GetProperty("stage").GetString());
        var sent = SentMapRequests();
        Assert.Equal(map.Count, sent.Count);
        foreach (var pass in map)
        {
            Assert.Contains(sent, r => r.System == pass.GetProperty("system").GetString() && string.Join("\n\n", r.Messages.Select(m => m.Content)) == pass.GetProperty("user").GetString());
        }
    }

    [Fact]
    public async Task TheInputsShowTheirTicksSizesAndWhatTheShareSwitchesHoldBack()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true, askBeforeSend = false, share = new { agenda = false } } });
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Anthropic, "test-key-not-real", CancellationToken.None);

        var preview = await _host.ResultAsync("generation.preview", new { recordingId = id, template = await _host.MeetingMinutesAsync("anthropic") });

        Assert.Equal("cloud", preview.GetProperty("provider").GetProperty("kind").GetString());
        Assert.Equal("Claude", preview.GetProperty("provider").GetProperty("name").GetString());
        Assert.False(preview.GetProperty("askBeforeSend").GetBoolean());
        var inputs = preview.GetProperty("inputs").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!);
        Assert.Equal(["transcript", "details", "participants", "agenda", "highlights", "attachments", "previousDocuments"], inputs.Keys.Take(7));
        var transcript = inputs["transcript"];
        Assert.True(transcript.GetProperty("ticked").GetBoolean());
        Assert.True(transcript.GetProperty("included").GetBoolean());
        Assert.True(transcript.GetProperty("characters").GetInt64() > 1000);
        Assert.Contains("segments", transcript.GetProperty("summary").GetString(), StringComparison.Ordinal);
        var agenda = inputs["agenda"];
        Assert.True(agenda.GetProperty("ticked").GetBoolean());
        Assert.False(agenda.GetProperty("included").GetBoolean());
        Assert.Equal(0, agenda.GetProperty("characters").GetInt64());
        Assert.StartsWith("Held back", agenda.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Contains(preview.GetProperty("redactions").EnumerateArray(), r => r.GetString() == "Agenda is held back: Settings › AI and privacy does not share it with external services.");
        Assert.All(preview.GetProperty("passes").EnumerateArray(), p => Assert.DoesNotContain("<agenda>", p.GetProperty("user").GetString(), StringComparison.Ordinal));
        Assert.Equal(0, _host.Providers.CloudCreated);
    }

    [Fact]
    public async Task ThePreviewOrOnePassIsCopiedThroughTheHostClipboard()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();
        var template = await _host.MeetingMinutesAsync();
        var preview = await _host.ResultAsync("generation.preview", new { recordingId = id, template });
        var first = preview.GetProperty("passes")[0];

        var whole = await _host.ResultAsync("generation.copyPreview", new { recordingId = id, template });
        var one = await _host.ResultAsync("generation.copyPreview", new { recordingId = id, template, passId = "p1" });
        var missing = await _host.ErrorAsync("generation.copyPreview", new { recordingId = id, template, passId = "p999" });

        Assert.Equal(2, _clipboard.Copies.Count);
        var all = _clipboard.Copies[0].Text;
        Assert.StartsWith("Read by Local model", all, StringComparison.Ordinal);
        Assert.Contains("Audio and video are never sent.", all, StringComparison.Ordinal);
        Assert.Contains(first.GetProperty("system").GetString()!, all, StringComparison.Ordinal);
        Assert.Null(_clipboard.Copies[0].Html);
        Assert.Equal(all.Length, whole.GetProperty("characters").GetInt32());
        Assert.Equal(preview.GetProperty("passes").GetArrayLength(), whole.GetProperty("passes").GetInt32());
        Assert.StartsWith("=== " + first.GetProperty("title").GetString(), _clipboard.Copies[1].Text, StringComparison.Ordinal);
        Assert.Equal(1, one.GetProperty("passes").GetInt32());
        Assert.Equal(BridgeErrorCodes.InvalidParams, missing.GetProperty("code").GetString());
        Assert.Equal(0, _host.Providers.LocalCreated + _host.Providers.CloudCreated);
    }

    [Fact]
    public async Task AFollowUpEmailIsCopiedAsAnEmailOnItsOwn()
    {
        _host.InstallLocalModel();
        var id = await _host.CreateMeetingAsync();
        var template = JsonNode.Parse((await _host.MeetingMinutesAsync()).GetRawText())!;
        template["rows"]!.AsArray().Add(JsonNode.Parse("""
            {"modules":[{"id":"e1","module":"followUpEmail","instructions":"","length":"medium","textSize":"normal","linkToTranscript":true,"customTitle":null,"customText":null,"email":{"tone":"warm","signOff":"Dana"}}]}
            """));
        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template = JsonDocument.Parse(template.ToJsonString()).RootElement });
        var done = await _host.FinishedAsync(start.GetProperty("jobId").GetString()!);
        var documentId = done.GetProperty("documentId").GetString()!;

        var copied = await _host.ResultAsync("documents.copyModule", new { recordingId = id, documentId, moduleId = "e1" });
        var unknown = await _host.ErrorAsync("documents.copyModule", new { recordingId = id, documentId, moduleId = "nosuch" });

        var copy = Assert.Single(_clipboard.Copies);
        Assert.StartsWith("To: ", copy.Text, StringComparison.Ordinal);
        Assert.Contains("\r\nSubject: Follow-up: Ledgerly weekly product sync (", copy.Text, StringComparison.Ordinal);
        Assert.Contains("\r\nAction items\r\n\r\n- Luis Brandt: ", copy.Text, StringComparison.Ordinal);
        Assert.EndsWith("Warm regards,\r\n\r\nDana\r\n", copy.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\[\d+:\d\d\]", copy.Text);
        Assert.StartsWith("<!doctype html>", copy.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1 class=\"paper-title\">", copy.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2 class=\"paper-h\">", copy.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-t=", copy.Html, StringComparison.Ordinal);
        Assert.Equal(copy.Text.Length, copied.GetProperty("characters").GetInt32());
        Assert.Equal(BridgeErrorCodes.InvalidParams, unknown.GetProperty("code").GetString());

        // The template keeps the email's tone and sign-off.
        var document = await _host.ResultAsync("documents.get", new { recordingId = id, documentId });
        Assert.Contains(document.GetProperty("document").GetProperty("rows").EnumerateArray().SelectMany(r => r.GetProperty("modules").EnumerateArray()), m => m.GetProperty("module").GetString() == "followUpEmail");
    }

    [Theory]
    [InlineData("""{"tone":"chummy","signOff":null}""", "email.tone")]
    [InlineData("""{"tone":"warm","signOff":"Line one\nLine two"}""", "email.signOff")]
    public async Task AnUnknownToneOrAMultiLineSignOffIsRefused(string email, string field)
    {
        var template = JsonNode.Parse((await _host.MeetingMinutesAsync()).GetRawText())!;
        template["rows"]!.AsArray().Add(JsonNode.Parse($$"""
            {"modules":[{"id":"e1","module":"followUpEmail","instructions":"","length":"medium","textSize":"normal","linkToTranscript":false,"customTitle":null,"customText":null,"email":{{email}}}]}
            """));

        var error = await _host.ErrorAsync("templates.save", new { template = JsonDocument.Parse(template.ToJsonString()).RootElement });

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.GetProperty("code").GetString());
        Assert.EndsWith(field, error.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmailsToneAndSignOffAreSavedWithTheTemplate()
    {
        var template = JsonNode.Parse((await _host.MeetingMinutesAsync()).GetRawText())!;
        template["id"] = string.Empty;
        template["name"] = "Minutes and email";
        template["rows"]!.AsArray().Add(JsonNode.Parse("""
            {"modules":[{"id":"e1","module":"followUpEmail","instructions":"","length":"short","textSize":"normal","linkToTranscript":false,"customTitle":null,"customText":null,"email":{"tone":"brief","signOff":" Dana "}}]}
            """));

        var saved = await _host.ResultAsync("templates.save", new { template = JsonDocument.Parse(template.ToJsonString()).RootElement });
        var loaded = await _host.ResultAsync("templates.get", new { templateId = saved.GetProperty("id").GetString() });

        var email = loaded.GetProperty("rows").EnumerateArray().SelectMany(r => r.GetProperty("modules").EnumerateArray()).Single(m => m.GetProperty("id").GetString() == "e1").GetProperty("email");
        Assert.Equal("""{"tone":"brief","signOff":"Dana"}""", email.GetRawText());
        Assert.DoesNotContain(loaded.GetProperty("rows").EnumerateArray().SelectMany(r => r.GetProperty("modules").EnumerateArray()), m => m.GetProperty("module").GetString() != "followUpEmail" && m.TryGetProperty("email", out _));
    }

    private List<AiRequest> SentMapRequests()
    {
        var provider = Assert.IsType<MeetingProvider>(_host.Providers.Last);
        return provider.Requests.Where(r => r.Purpose.StartsWith("map.", StringComparison.Ordinal)).ToList();
    }
}
