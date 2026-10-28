using System.Text.Json;
using Memento.AI;
using Memento.Core.Bridge;
using Memento.Core.Secrets;
using Memento.Core.Tests.Fakes;
using Memento.Generation.Ask;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// 2.9.0 Ask through the bridge: the conversation file, streaming, citations limited to what was sent, stopping
/// mid-stream, provider errors, the privacy rules (external AI off, the share switches, ask before every send) and the
/// parameter checks. Every provider is a scripted stand-in; nothing is sent anywhere.
/// </summary>
public sealed class AskBridgeTests : IDisposable
{
    private readonly M4Host _host = new();
    private readonly AskProvider _provider = new();

    public AskBridgeTests()
    {
        _host.Providers.Make = kind => kind == AiProviderKind.Local ? _provider : new AskProvider(kind);
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ARecordingStartsWithAnEmptyConversation()
    {
        var id = await _host.CreateMeetingAsync();

        var result = await _host.ResultAsync("ask.get", new { recordingId = id });

        Assert.Equal(id, result.GetProperty("recordingId").GetString());
        Assert.Equal(0, result.GetProperty("turns").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("jobId").ValueKind);
        Assert.Equal(DomainErrorCodes.ProjectNotFound, (await _host.ErrorAsync("ask.get", new { recordingId = "nope" })).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheLocalModelAnswersStreamingAndTheAnswerIsSavedWithOnlyTheCitationsThatWereSent()
    {
        var id = await _host.CreateMeetingAsync();
        _host.InstallLocalModel();

        var sent = await _host.ResultAsync("ask.send", new { recordingId = id, question = "What did the team agree on?", providerId = "local" });
        Assert.False(sent.GetProperty("confirmationRequired").GetBoolean());
        Assert.Equal("What did the team agree on?", sent.GetProperty("question").GetProperty("text").GetString());
        var final = await FinishedAsync(sent.GetProperty("jobId").GetString()!);

        Assert.Equal("done", final.GetProperty("kind").GetString());
        var events = _host.Events("ask.progress");
        Assert.Equal("context", events[0].GetProperty("kind").GetString());
        // The synthetic meeting is longer than the local model's budget, so its best sections go.
        var context = events[0].GetProperty("context");
        Assert.Equal("sections", context.GetProperty("mode").GetString());
        Assert.NotEmpty(context.GetProperty("sections").EnumerateArray());
        var streamed = string.Concat(events.Where(e => e.GetProperty("kind").GetString() == "token").Select(e => e.GetProperty("text").GetString()));
        var firstSent = context.GetProperty("sections")[0].GetProperty("firstLine").GetInt32();
        Assert.Equal($"The team agreed on it [{firstSent}] and also [99999].", streamed);
        var turn = final.GetProperty("turn");
        var citation = Assert.Single(turn.GetProperty("citations").EnumerateArray());
        Assert.Equal(firstSent, citation.GetProperty("line").GetInt32());
        Assert.False(string.IsNullOrEmpty(citation.GetProperty("segmentId").GetString()));
        Assert.True(turn.GetProperty("stayedOnPc").GetBoolean());

        var request = Assert.Single(_provider.Requests);
        Assert.Equal("ask.answer", request.Purpose);
        Assert.StartsWith("<recording scope=\"the sections of the transcript that best match the question\">", request.Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("<transcript section=\"", request.Messages[^1].Content, StringComparison.Ordinal);

        var conversation = await _host.ResultAsync("ask.get", new { recordingId = id });
        Assert.Equal(["user", "assistant"], conversation.GetProperty("turns").EnumerateArray().Select(t => t.GetProperty("role").GetString()));
        Assert.Equal(["t1", "t2"], conversation.GetProperty("turns").EnumerateArray().Select(t => t.GetProperty("id").GetString()));
        var file = JsonDocument.Parse(await File.ReadAllTextAsync(_host.Get<AskConversationStore>().PathOf(id))).RootElement;
        Assert.Equal(1, file.GetProperty("schemaVersion").GetInt32());
        Assert.DoesNotContain("<transcript>", file.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Summary == "Question answered by Local model");
    }

    [Fact]
    public async Task AFollowUpCarriesTheEarlierTurnsAsConversation()
    {
        var id = await _host.CreateMeetingAsync();
        _host.InstallLocalModel();
        await FinishedAsync((await _host.ResultAsync("ask.send", new { recordingId = id, question = "First?" })).GetProperty("jobId").GetString()!);

        await FinishedAsync((await _host.ResultAsync("ask.send", new { recordingId = id, question = "And then?" })).GetProperty("jobId").GetString()!);

        var second = _provider.Requests[^1];
        Assert.Equal([AiRole.User, AiRole.Assistant, AiRole.User], second.Messages.Select(m => m.Role));
        Assert.Equal("First?", second.Messages[0].Content);
        Assert.Equal(4, (await _host.ResultAsync("ask.get", new { recordingId = id })).GetProperty("turns").GetArrayLength());
    }

    [Fact]
    public async Task StoppingMidStreamKeepsWhatArrivedAsAStoppedAnswer()
    {
        var id = await _host.CreateMeetingAsync();
        _host.InstallLocalModel();
        _provider.Hold = new TaskCompletionSource();

        var jobId = (await _host.ResultAsync("ask.send", new { recordingId = id, question = "Who spoke?" })).GetProperty("jobId").GetString()!;
        await _provider.FirstPiece.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(jobId, (await _host.ResultAsync("ask.get", new { recordingId = id })).GetProperty("jobId").GetString());
        Assert.Equal(DomainErrorCodes.AskBusy, (await _host.ErrorAsync("ask.send", new { recordingId = id, question = "Again?" })).GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.AskBusy, (await _host.ErrorAsync("ask.clear", new { recordingId = id })).GetProperty("code").GetString());
        await _host.ResultAsync("ask.cancel", new { jobId });
        var final = await FinishedAsync(jobId);

        Assert.Equal("cancelled", final.GetProperty("kind").GetString());
        Assert.True(final.GetProperty("turn").GetProperty("stopped").GetBoolean());
        Assert.Equal("The team", final.GetProperty("turn").GetProperty("text").GetString());
        Assert.Equal(DomainErrorCodes.AskNotFound, (await _host.ErrorAsync("ask.cancel", new { jobId })).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AProviderErrorEndsWithItsCodeAndSpecificMessageAndSavesNoAnswer()
    {
        var id = await _host.CreateMeetingAsync();
        _host.InstallLocalModel();
        _provider.Failure = new AiException(AiErrors.NoNetwork("Local model"));

        var final = await FinishedAsync((await _host.ResultAsync("ask.send", new { recordingId = id, question = "Anything?" })).GetProperty("jobId").GetString()!);

        Assert.Equal("failed", final.GetProperty("kind").GetString());
        Assert.Equal(AiErrorCodes.Network, final.GetProperty("code").GetString());
        Assert.Contains("could not be reached", final.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(["user"], (await _host.ResultAsync("ask.get", new { recordingId = id })).GetProperty("turns").EnumerateArray().Select(t => t.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task AnExternalProviderWithExternalAiOffIsRefusedBeforeAnythingIsRead()
    {
        var id = await _host.CreateMeetingAsync();

        var error = await _host.ErrorAsync("ask.send", new { recordingId = id, question = "Hi?", providerId = "anthropic" });

        Assert.Equal(DomainErrorCodes.AiDisabled, error.GetProperty("code").GetString());
        Assert.Contains("nothing was sent", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _host.Providers.CloudCreated);
        Assert.False(File.Exists(_host.Get<AskConversationStore>().PathOf(id)));
    }

    [Fact]
    public async Task AskBeforeEverySendHoldsACloudQuestionAndADeclineSendsNothing()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true } });
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Anthropic, "test-key-not-real", CancellationToken.None);

        var held = await _host.ResultAsync("ask.send", new { recordingId = id, question = "Summary?", providerId = "anthropic" });
        Assert.True(held.GetProperty("confirmationRequired").GetBoolean());
        var summary = held.GetProperty("summary");
        Assert.Equal("Claude", summary.GetProperty("providerName").GetString());
        Assert.True(summary.GetProperty("bytes").GetInt64() > 0);
        Assert.Equal("whole", summary.GetProperty("context").GetProperty("mode").GetString());
        await _host.ResultAsync("ask.confirm", new { jobId = held.GetProperty("jobId").GetString(), approved = false });
        Assert.Equal("cancelled", (await FinishedAsync(held.GetProperty("jobId").GetString()!)).GetProperty("kind").GetString());
        Assert.Equal(0, _host.Providers.CloudCreated);
        Assert.Equal(0, (await _host.ResultAsync("ask.get", new { recordingId = id })).GetProperty("turns").GetArrayLength());

        var again = await _host.ResultAsync("ask.send", new { recordingId = id, question = "Summary?", providerId = "anthropic" });
        await _host.ResultAsync("ask.confirm", new { jobId = again.GetProperty("jobId").GetString(), approved = true });
        var final = await FinishedAsync(again.GetProperty("jobId").GetString()!);
        Assert.Equal("done", final.GetProperty("kind").GetString());
        Assert.False(final.GetProperty("turn").GetProperty("stayedOnPc").GetBoolean());
        Assert.Equal(1, _host.Providers.CloudCreated);
    }

    [Fact]
    public async Task TheShareSwitchesDecideWhetherTheTranscriptMayLeave()
    {
        var id = await _host.CreateMeetingAsync();
        await _host.ResultAsync("settings.set", new { ai = new { enabled = true, askBeforeSend = false, share = new { transcript = false } } });
        await _host.Get<ISecretStore>().SetKeyAsync(AiProviders.Anthropic, "test-key-not-real", CancellationToken.None);

        var error = await _host.ErrorAsync("ask.send", new { recordingId = id, question = "Hi?", providerId = "anthropic" });

        Assert.Equal(DomainErrorCodes.AskNoTranscript, error.GetProperty("code").GetString());
        Assert.Equal(0, _host.Providers.CloudCreated);
    }

    [Theory]
    [InlineData("   ", null, BridgeErrorCodes.InvalidParams)]
    [InlineData("Fine?", "gemini", BridgeErrorCodes.InvalidParams)]
    public async Task BadParametersAreRefused(string question, string? provider, string code)
    {
        var id = await _host.CreateMeetingAsync();

        Assert.Equal(code, (await _host.ErrorAsync("ask.send", new { recordingId = id, question, providerId = provider })).GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, (await _host.ErrorAsync("ask.send", new { recordingId = id, question = new string('x', AskService.MaxQuestionLength + 1) })).GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, (await _host.CallAsync("ask.send", new { recordingId = id })).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.AskNotFound, (await _host.ErrorAsync("ask.confirm", new { jobId = "a0", approved = true })).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ClearDeletesTheConversation()
    {
        var id = await _host.CreateMeetingAsync();
        _host.InstallLocalModel();
        await FinishedAsync((await _host.ResultAsync("ask.send", new { recordingId = id, question = "First?" })).GetProperty("jobId").GetString()!);

        var cleared = await _host.ResultAsync("ask.clear", new { recordingId = id });

        Assert.Equal(0, cleared.GetProperty("turns").GetArrayLength());
        Assert.False(File.Exists(_host.Get<AskConversationStore>().PathOf(id)));
        Assert.Contains(await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None), h => h.Summary == "Ask conversation cleared");
    }

    [Fact]
    public async Task ADamagedFileIsKeptAsideAndANewerOneIsNeverWritten()
    {
        var id = await _host.CreateMeetingAsync();
        _host.InstallLocalModel();
        var path = _host.Get<AskConversationStore>().PathOf(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json");

        Assert.Equal(0, (await _host.ResultAsync("ask.get", new { recordingId = id })).GetProperty("turns").GetArrayLength());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "conversation.damaged-*.json"));

        const string Newer = """{ "schemaVersion": 2, "turns": [ { "id": "t1", "role": "user", "text": "Kept", "at": "2026-10-01T10:00:00+00:00", "mood": "new" } ] }""";
        await File.WriteAllTextAsync(path, Newer);
        Assert.Equal("Kept", (await _host.ResultAsync("ask.get", new { recordingId = id })).GetProperty("turns")[0].GetProperty("text").GetString());
        Assert.Equal(DomainErrorCodes.AskNewerVersion, (await _host.ErrorAsync("ask.send", new { recordingId = id, question = "Hi?" })).GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.AskNewerVersion, (await _host.ErrorAsync("ask.clear", new { recordingId = id })).GetProperty("code").GetString());
        Assert.Equal(Newer, await File.ReadAllTextAsync(path));
        Assert.Empty(_provider.Requests);
    }

    [Fact]
    public async Task ATranscriptBeyondTheBudgetIsSearchedAndTheSectionsAreNamed()
    {
        var id = await _host.CreateMeetingAsync();
        _host.InstallLocalModel();
        var status = _host.Get<Memento.Generation.Ai.ProviderRegistry>().Status("local");
        var payload = Memento.AI.Payload.PayloadComposer.Compose(
            new Memento.AI.Payload.PayloadInputs
            {
                Segments = Enumerable.Range(1, 4000).Select(i => new Memento.AI.Payload.PayloadSegment("s" + i, i, i + 1, "a", i == 3000 ? "The kangaroo budget was approved." : "General chatter about the week and many other things.")).ToList(),
            },
            AskService.Selection);

        var plan = AskService.Plan(status, new AskProvider(context: 8192), payload, [], [], "Was the kangaroo budget approved?");

        Assert.Equal("sections", plan.Info.Mode);
        Assert.Contains(plan.Info.Sections, s => s.Matched && s.FirstLine <= 3000 && s.LastLine >= 3000);
        Assert.True(new AskProvider().CountTokens(Memento.AI.AiRequestText.Render(plan.Request)) + plan.Request.MaxOutputTokens <= 8192);
    }

    private async Task<JsonElement> FinishedAsync(string jobId)
    {
        JsonElement? final = null;
        await TestRecordings.WaitUntilAsync(
            () =>
            {
                final = _host.Events("ask.progress").LastOrDefault(e => e.GetProperty("jobId").GetString() == jobId && e.GetProperty("kind").GetString() is "done" or "failed" or "cancelled");
                return final is { ValueKind: JsonValueKind.Object };
            },
            "the answer to finish",
            20_000);
        return final!.Value;
    }
}
