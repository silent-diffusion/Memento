using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Memento.AI.Http;
using Memento.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Memento.AI.OpenAI;

/// <summary>
/// ChatGPT models through the Responses API (<c>POST /v1/responses</c>, which OpenAI recommends for new projects),
/// always streamed, with <c>store: false</c> so OpenAI keeps no copy of the conversation for later retrieval. JSON
/// answers use <c>text.format</c> with a strict JSON schema. Raw HTTP, like the Anthropic provider, so the app keeps
/// its .NET 8 assemblies.
/// </summary>
public sealed partial class OpenAiProvider : IAiProvider
{
    public const string ProviderId = AiProviders.OpenAi;

    private readonly ISecretReader _secrets;
    private readonly OpenAiOptions _options;
    private readonly CloudRequestRunner _runner;
    private readonly Uri _responsesUrl;

    public OpenAiProvider(HttpClient http, ISecretReader secrets, OpenAiOptions options, ILogger<OpenAiProvider> logger, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _secrets = secrets;
        _options = options;
        _responsesUrl = new Uri(options.BaseUrl, "v1/responses");
        _runner = new CloudRequestRunner(http, options.Http, time ?? TimeProvider.System, EstimatingTokenCounter.OpenAi, logger);
        Capabilities = new AiCapabilities(options.ContextTokens, options.MaxOutputTokens, SupportsJsonSchema: true, SupportsGrammar: false, SupportsStreaming: true, ExactTokenCounts: false);
    }

    public string Id => ProviderId;

    public string DisplayName => AiProviders.DisplayName(AiProviders.OpenAi);

    public AiProviderKind Kind => AiProviderKind.Cloud;

    public string Model => _options.Model;

    public AiCapabilities Capabilities { get; }

    public int CountTokens(string text) => EstimatingTokenCounter.OpenAi.Count(text);

    public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.HasKey(ProviderId)
            ? AiReadiness.Ready(string.Create(CultureInfo.InvariantCulture, $"{DisplayName} ({Model})"))
            : AiReadiness.NotReady(AiErrors.NoKey(DisplayName)));

    public async Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (request.Grammar is not null && request.JsonSchema is null)
        {
            throw new ArgumentException("OpenAI constrains output with a JSON schema, not a GBNF grammar; set JsonSchema.", nameof(request));
        }

        var key = await _secrets.GetKeyAsync(ProviderId, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new AiException(AiErrors.NoKey(DisplayName));
        }

        var body = BuildBody(request);
        var call = new CloudCall(
            DisplayName,
            request.Purpose,
            () => CreateMessage(body, key),
            ReadStreamAsync,
            MapFailure,
            code => code is "rate_limit_exceeded" ? AiErrors.RateLimited(DisplayName, null, null, code) : AiErrors.StreamFailed(DisplayName, code));
        var outcome = await _runner.RunAsync(call, progress, cancellationToken);
        var result = outcome.Result;
        return new AiResponse(
            Id,
            result.Model ?? Model,
            result.Text,
            CloudJson.ParseAnswer(request, result, DisplayName),
            result.StopReason,
            result.ProviderStopReason,
            result.Usage,
            outcome.Timings,
            AiRequestHash.Compute(request, Id, Model));
    }

    internal byte[] BuildBody(AiRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _options.Model);
            writer.WriteBoolean("stream", true);
            writer.WriteBoolean("store", false);
            writer.WriteNumber("max_output_tokens", (int)Math.Min((long)request.MaxOutputTokens + _options.Http.ThinkingHeadroomTokens, _options.MaxOutputTokens));
            if (!string.IsNullOrEmpty(request.System))
            {
                writer.WriteString("instructions", request.System);
            }

            writer.WriteStartArray("input");
            foreach (var message in request.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.Role == AiRole.User ? "user" : "assistant");
                writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (_options.SendTemperature && request.Temperature is { } temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            if (_options.ReasoningEffort is { } effort)
            {
                writer.WriteStartObject("reasoning");
                writer.WriteString("effort", effort);
                writer.WriteEndObject();
            }

            if (request.JsonSchema is { } schema)
            {
                writer.WriteStartObject("text");
                writer.WriteStartObject("format");
                writer.WriteString("type", "json_schema");
                writer.WriteString("name", SchemaName(request.SchemaName));
                writer.WritePropertyName("schema");
                schema.WriteTo(writer);
                writer.WriteBoolean("strict", true);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>OpenAI schema names allow letters, digits, underscores and dashes, up to 64 characters.</summary>
    internal static string SchemaName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        var cleaned = builder.ToString();
        return cleaned.Length == 0 ? "result" : cleaned[..Math.Min(64, cleaned.Length)];
    }

    private HttpRequestMessage CreateMessage(byte[] body, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _responsesUrl)
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return message;
    }

    private async Task<CloudStreamResult> ReadStreamAsync(Stream stream, CloudStreamContext context, CancellationToken cancellationToken)
    {
        string? model = null, status = null;
        var stop = AiStopReason.Other;
        var usage = AiUsage.None;
        var refused = false;
        var finished = false;
        await foreach (var item in SseReader.ReadAsync(stream, _options.Http.StreamIdleTimeout, cancellationToken))
        {
            if (item.Data == "[DONE]")
            {
                break;
            }

            using var document = JsonDocument.Parse(item.Data);
            var root = document.RootElement;
            var type = CloudJson.String(root, "type") ?? item.Event;
            switch (type)
            {
                case "response.created" or "response.in_progress" when root.TryGetProperty("response", out var created):
                    model = CloudJson.String(created, "model") ?? model;
                    break;
                case "response.output_text.delta":
                    context.Append(CloudJson.String(root, "delta"));
                    break;
                case "response.refusal.delta" or "response.refusal.done":
                    refused = true;
                    break;
                case "response.completed" or "response.incomplete" when root.TryGetProperty("response", out var response):
                    model = CloudJson.String(response, "model") ?? model;
                    status = CloudJson.String(response, "status") ?? type["response.".Length..];
                    usage = ReadUsage(response);
                    var reason = response.TryGetProperty("incomplete_details", out var details) ? CloudJson.String(details, "reason") : null;
                    stop = refused ? AiStopReason.Refusal : status switch
                    {
                        "completed" => AiStopReason.Completed,
                        "incomplete" when reason is "max_output_tokens" => AiStopReason.MaxTokens,
                        "incomplete" when reason is "content_filter" => AiStopReason.Refusal,
                        _ => AiStopReason.Other,
                    };
                    status = reason is null ? status : status + ":" + reason;
                    finished = true;
                    break;
                case "response.failed" when root.TryGetProperty("response", out var failed):
                    var failedCode = failed.TryGetProperty("error", out var failedError) ? CloudJson.String(failedError, "code") : null;
                    throw new CloudStreamException(failedCode, failedCode is "server_error" or "rate_limit_exceeded");
                case "error":
                    var code = CloudJson.String(root, "code") ?? (root.TryGetProperty("error", out var error) ? CloudJson.String(error, "code") ?? CloudJson.String(error, "type") : null);
                    throw new CloudStreamException(code, code is "server_error" or "rate_limit_exceeded");
                default:
                    break;
            }

            if (finished)
            {
                break;
            }
        }

        if (!finished)
        {
            throw new CloudStreamException("stream ended early", retryable: false);
        }

        return new CloudStreamResult(context.Text, model, stop, status, usage);
    }

    private static AiUsage ReadUsage(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return AiUsage.None;
        }

        var cached = usage.TryGetProperty("input_tokens_details", out var details) ? CloudJson.Int(details, "cached_tokens") : null;
        return new AiUsage(CloudJson.Int(usage, "input_tokens") ?? 0, CloudJson.Int(usage, "output_tokens") ?? 0, cached);
    }

    private AiError MapFailure(CloudHttpFailure failure) => failure.Status switch
    {
        401 => AiErrors.InvalidKey(DisplayName, failure.Status),
        403 => AiErrors.NotPermitted(DisplayName, failure.Status, failure.ErrorCode ?? failure.ErrorType),
        429 when failure.ErrorCode is "insufficient_quota" || failure.ErrorType is "insufficient_quota" => AiErrors.QuotaExhausted(DisplayName, failure.Status, "insufficient_quota"),
        429 => AiErrors.RateLimited(DisplayName, failure.RetryAfter, failure.Status, failure.ErrorCode ?? failure.ErrorType),
        413 => AiErrors.ContentTooLong(DisplayName, status: failure.Status),
        400 when failure.ErrorCode is "context_length_exceeded" => TooLong(failure),
        >= 500 => AiErrors.ServerFailed(DisplayName, failure.Status, failure.ErrorCode ?? failure.ErrorType),
        _ => AiErrors.Rejected(DisplayName, failure.Status, failure.ErrorCode ?? failure.ErrorType),
    };

    private AiError TooLong(CloudHttpFailure failure)
    {
        // "This model's maximum context length is 1050000 tokens. However, your messages resulted in 1100000 tokens."
        var limit = MaximumContext().Match(failure.ErrorMessage ?? string.Empty);
        var actual = ResultedIn().Match(failure.ErrorMessage ?? string.Empty);
        return limit.Success && actual.Success
            && int.TryParse(limit.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)
            && int.TryParse(actual.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens)
            ? AiErrors.ContentTooLong(DisplayName, tokens, max, failure.Status)
            : AiErrors.ContentTooLong(DisplayName, status: failure.Status);
    }

    [GeneratedRegex(@"maximum context length is (\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex MaximumContext();

    [GeneratedRegex(@"resulted in (\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ResultedIn();
}
