using System.Net.Http.Headers;
using System.Text;
using Memento.AI.Http;

namespace Memento.AI.Tests.Cloud;

public sealed class CloudPlumbingTests
{
    [Fact]
    public async Task SseReaderJoinsDataLinesAndSkipsComments()
    {
        var text = ": keep-alive\nevent: a\ndata: one\ndata: two\n\nevent: b\ndata: {\"x\":1}\n\ndata: tail";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var events = new List<SseEvent>();

        await foreach (var item in SseReader.ReadAsync(stream, TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Equal([new SseEvent("a", "one\ntwo"), new SseEvent("b", "{\"x\":1}"), new SseEvent(string.Empty, "tail")], events);
    }

    [Fact]
    public async Task SseReaderEndsLinesAtLfCrOrCrLf()
    {
        var text = "event: a\r\ndata: one\r\rdata: two\n\r\ndata: three\r";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var events = new List<SseEvent>();

        await foreach (var item in SseReader.ReadAsync(stream, TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Equal([new SseEvent("a", "one"), new SseEvent(string.Empty, "two"), new SseEvent(string.Empty, "three")], events);
    }

    [Fact]
    public async Task SseReaderRefusesALineOrAnEventPastTheLimit()
    {
        static async Task ReadAllAsync(string text)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            await foreach (var unused in SseReader.ReadAsync(stream, TimeSpan.FromSeconds(30), CancellationToken.None))
            {
            }
        }

        // One endless line, and an event built from many short data lines.
        await Assert.ThrowsAsync<CloudAnswerTooLongException>(() => ReadAllAsync("data: " + new string('x', SseReader.MaxEventChars + 1)));
        var line = "data: " + new string('y', 1024 * 1024) + "\n";
        await Assert.ThrowsAsync<CloudAnswerTooLongException>(() => ReadAllAsync(string.Concat(Enumerable.Repeat(line, (SseReader.MaxEventChars / (1024 * 1024)) + 1))));
    }

    [Fact]
    public void TheStreamContextKeepsAtMostTheAnswerLimit()
    {
        var context = new CloudStreamContext(null, 1, TimeProvider.System, TimeProvider.System.GetTimestamp(), EstimatingTokenCounter.Generic);
        context.Append(new string('a', CloudStreamContext.MaxAnswerChars - 1));
        context.Append("b");

        Assert.Throws<CloudAnswerTooLongException>(() => context.Append("c"));
        Assert.Equal(CloudStreamContext.MaxAnswerChars, context.Text.Length);
    }

    [Fact]
    public void RetryAfterReadsMillisecondsSecondsAndDates()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        using var a = new HttpResponseMessage();
        a.Headers.TryAddWithoutValidation("retry-after-ms", "250");
        a.Headers.TryAddWithoutValidation("retry-after", "9");
        using var b = new HttpResponseMessage();
        b.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
        using var c = new HttpResponseMessage();
        c.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(30));
        using var d = new HttpResponseMessage();
        d.Headers.TryAddWithoutValidation("retry-after", "1.5");
        using var e = new HttpResponseMessage();

        Assert.Equal(TimeSpan.FromMilliseconds(250), RetryAfter.Read(a.Headers, now));
        Assert.Equal(TimeSpan.FromSeconds(12), RetryAfter.Read(b.Headers, now));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfter.Read(c.Headers, now));
        Assert.Equal(TimeSpan.FromSeconds(1.5), RetryAfter.Read(d.Headers, now));
        Assert.Null(RetryAfter.Read(e.Headers, now));
    }

    [Theory]
    [InlineData(429, null, true)]
    [InlineData(429, "insufficient_quota", false)]
    [InlineData(500, null, true)]
    [InlineData(529, null, true)]
    [InlineData(503, null, true)]
    [InlineData(400, null, false)]
    [InlineData(401, null, false)]
    [InlineData(404, null, false)]
    public void OnlyNotProcessedAnswersAreRetried(int status, string? code, bool retryable) =>
        Assert.Equal(retryable, CloudRequestRunner.IsRetryable(new CloudHttpFailure(status, null, code, null, null)));

    [Fact]
    public void RedactorRemovesTheKeyAndKeyShapedText()
    {
        const string key = "test-only-redaction-key-123456";
        var text = $"bad key {key}; provided: sk-test_****abcd and sess-abcdef123 but not skip-this";

        var redacted = SecretRedactor.Redact(text, key)!;

        Assert.DoesNotContain(key, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sess-abcdef123", redacted, StringComparison.Ordinal);
        Assert.Contains("skip-this", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryErrorCodeHasCopyThatNamesWhatIsSafe()
    {
        var errors = new[]
        {
            AiErrors.NoKey("Claude"),
            AiErrors.InvalidKey("Claude", 401),
            AiErrors.RateLimited("Claude", TimeSpan.FromSeconds(30)),
            AiErrors.NoNetwork("Claude"),
            AiErrors.ServerFailed("Claude", 500),
            AiErrors.ContentTooLong("Claude", 10, 5),
            AiErrors.UnknownModel("Claude", "claude-x", 404),
            AiErrors.Cancelled("Claude"),
            AiErrors.ModelNotInstalled("Local model", "Qwen3.5 4B"),
            AiErrors.NotEnoughVram("Local model", "Qwen3.5 4B", 2L << 30, 3L << 30),
            AiErrors.WorkerCrashed("Local model", "Qwen3.5 4B"),
            AiErrors.NotEnoughMemory("Local model", "Qwen3.5 35B-A3B", 25L << 30, 11L << 30),
            AiErrors.UnloadedForRecording("Local model", "Qwen3.5 35B-A3B", 3L << 29),
            AiErrors.ServerUnreachable("Your own server", "http://localhost:11434", refused: true),
            AiErrors.ServerNotCompatible("Your own server", "http://localhost:11434", 404, "/v1/chat/completions"),
            AiErrors.ServerNeedsKey("Your own server", "http://192.168.1.20:8000", 401, hasKey: false),
            AiErrors.ServerModelUnknown("Your own server", "http://localhost:11434", "qwen9:1b", 404),
            AiErrors.ServerInsecure("Your own server", "http://203.0.113.10:8000"),
        };

        Assert.Equal(AiErrorCodes.All.ToHashSet(), errors.Select(e => e.Code).ToHashSet());
        Assert.All(errors, e => Assert.Contains("no document was changed", e.Message, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Claude could not be reached: no network. Nothing was sent twice and no document was changed.", AiErrors.NoNetwork("Claude").Message);
        Assert.Equal("The graphics card has 2.0 GB of free memory and Qwen3.5 4B needs 3.0 GB. Nothing left this PC and no document was changed. Close apps that use the graphics card, or run the model on the processor.", errors[9].Message);
        Assert.Contains("Try again in 30 seconds.", errors[2].Message, StringComparison.Ordinal);

        // 2.5.0, "Your own server": the brief's sentence first, then what is safe and the fix.
        Assert.Equal("Nothing is listening at http://localhost:11434. Nothing was sent and no document was changed. Start Ollama or LM Studio, or check the address in Settings › AI and privacy.", errors[13].Message);
        Assert.Equal("The server at http://localhost:11434 does not offer a model called 'qwen9:1b'. Nothing was generated and no document was changed. Choose one of its models in Settings › AI and privacy (Test connection lists them), or check the name.", errors[16].Message);
        Assert.StartsWith("http://203.0.113.10:8000 is outside this PC and your local network, and Memento sends there only over https.", errors[17].Message, StringComparison.Ordinal);
        Assert.Equal("Qwen3.5 35B-A3B needs 25 GB of memory; this PC has 11 GB free. Nothing left this PC and no document was changed. Close other programs or choose a smaller model.", errors[11].Message);
        Assert.Equal("Qwen3.5 35B-A3B was unloaded because a recording started and this PC had 1.5 GB of memory free, too little to keep both. The recording is not affected. Nothing left this PC and no document was changed. Generate the document again when the recording has finished, or choose a smaller model.", errors[12].Message);
    }
}
