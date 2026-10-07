using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Memento.AI.Http;
using Memento.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Memento.AI.Anthropic;

/// <summary>
/// Claude through the Messages API (<c>POST /v1/messages</c>, <c>anthropic-version: 2023-06-01</c>), always streamed.
/// JSON answers use structured outputs (<c>output_config.format</c> with a JSON schema); refusals are reported as
/// <see cref="AiStopReason.Refusal"/> and, with the server-side fallback on, re-run on Anthropic's fallback model.
/// Raw HTTP rather than the Anthropic .NET SDK: the SDK depends on System.Text.Json 10 and
/// Microsoft.Extensions.AI.Abstractions 10, and the app deliberately keeps its .NET 8 assemblies (THIRD-PARTY.md).
/// </summary>
public sealed partial class AnthropicProvider : IAiProvider
{
    public const string ProviderId = AiProviders.Anthropic;
    internal const string ApiVersion = "2023-06-01";
    internal const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly ISecretReader _secrets;
    private readonly AnthropicOptions _options;
    private readonly CloudRequestRunner _runner;
    private readonly Uri _messagesUrl;

    public AnthropicProvider(HttpClient http, ISecretReader secrets, AnthropicOptions options, ILogger<AnthropicProvider> logger, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _secrets = secrets;
        _options = options;
        _messagesUrl = new Uri(options.BaseUrl, "v1/messages");
        _runner = new CloudRequestRunner(http, options.Http, time ?? TimeProvider.System, EstimatingTokenCounter.Claude, logger);
        Capabilities = new AiCapabilities(options.ContextTokens, options.MaxOutputTokens, SupportsJsonSchema: true, SupportsGrammar: false, SupportsStreaming: true, ExactTokenCounts: false);
    }

    public string Id => ProviderId;

    public string DisplayName => AiProviders.DisplayName(AiProviders.Anthropic);

    public AiProviderKind Kind => AiProviderKind.Cloud;

    public string Model => _options.Model;

    public AiCapabilities Capabilities { get; }

    public int CountTokens(string text) => EstimatingTokenCounter.Claude.Count(text);

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
            throw new ArgumentException("Claude constrains output with a JSON schema, not a GBNF grammar; set JsonSchema.", nameof(request));
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
            type => type is "rate_limit_error" ? AiErrors.RateLimited(DisplayName, null, null, type) : AiErrors.StreamFailed(DisplayName, type));
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
            AiRequestHash.Compute(request, Id, Model),
            result.FellBack);
    }

    internal byte[] BuildBody(AiRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _options.Model);
            writer.WriteNumber("max_tokens", (int)Math.Min((long)request.MaxOutputTokens + _options.Http.ThinkingHeadroomTokens, _options.MaxOutputTokens));
            writer.WriteBoolean("stream", true);
            if (!string.IsNullOrEmpty(request.System))
            {
                writer.WriteString("system", request.System);
            }

            writer.WriteStartArray("messages");
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

            if (_options.Effort is not null || request.JsonSchema is not null)
            {
                writer.WriteStartObject("output_config");
                if (_options.Effort is { } effort)
                {
                    writer.WriteString("effort", effort);
                }

                if (request.JsonSchema is { } schema)
                {
                    writer.WriteStartObject("format");
                    writer.WriteString("type", "json_schema");
                    writer.WritePropertyName("schema");
                    schema.WriteTo(writer);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            if (_options.UseServerSideFallback)
            {
                writer.WriteString("fallbacks", "default");
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private HttpRequestMessage CreateMessage(byte[] body, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _messagesUrl)
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.TryAddWithoutValidation("x-api-key", key);
        message.Headers.Add("anthropic-version", ApiVersion);
        if (_options.UseServerSideFallback)
        {
            message.Headers.Add("anthropic-beta", FallbackBeta);
        }

        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return message;
    }

    private async Task<CloudStreamResult> ReadStreamAsync(Stream stream, CloudStreamContext context, CancellationToken cancellationToken)
    {
        string? model = null, stopReason = null;
        int input = 0, output = 0;
        int? cached = null;
        var fellBack = false;
        var finished = false;
        await foreach (var item in SseReader.ReadAsync(stream, _options.Http.StreamIdleTimeout, cancellationToken))
        {
            using var document = JsonDocument.Parse(item.Data);
            var root = document.RootElement;
            switch (CloudJson.String(root, "type"))
            {
                case "message_start" when root.TryGetProperty("message", out var message):
                    model = CloudJson.String(message, "model") ?? model;
                    if (message.TryGetProperty("usage", out var startUsage))
                    {
                        input = CloudJson.Int(startUsage, "input_tokens") ?? input;
                        cached = CloudJson.Int(startUsage, "cache_read_input_tokens") ?? cached;
                    }

                    break;
                case "content_block_start" when root.TryGetProperty("content_block", out var block):
                    var blockType = CloudJson.String(block, "type");
                    fellBack |= blockType == "fallback";
                    if (blockType == "text")
                    {
                        context.Append(CloudJson.String(block, "text"));
                    }

                    break;
                case "content_block_delta" when root.TryGetProperty("delta", out var delta):
                    if (CloudJson.String(delta, "type") == "text_delta")
                    {
                        context.Append(CloudJson.String(delta, "text"));
                    }

                    break;
                case "message_delta":
                    if (root.TryGetProperty("delta", out var messageDelta))
                    {
                        stopReason = CloudJson.String(messageDelta, "stop_reason") ?? stopReason;
                    }

                    if (root.TryGetProperty("usage", out var usage))
                    {
                        output = CloudJson.Int(usage, "output_tokens") ?? output;
                        input = CloudJson.Int(usage, "input_tokens") ?? input;
                    }

                    break;
                case "message_stop":
                    finished = true;
                    break;
                case "error":
                    var errorType = root.TryGetProperty("error", out var error) ? CloudJson.String(error, "type") : null;
                    throw new CloudStreamException(errorType, errorType is "overloaded_error" or "api_error" or "rate_limit_error");
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

        return new CloudStreamResult(context.Text, model, MapStopReason(stopReason), stopReason, new AiUsage(input, output, cached), fellBack);
    }

    internal static AiStopReason MapStopReason(string? reason) => reason switch
    {
        "end_turn" or "stop_sequence" => AiStopReason.Completed,
        "max_tokens" or "model_context_window_exceeded" => AiStopReason.MaxTokens,
        "refusal" => AiStopReason.Refusal,
        _ => AiStopReason.Other,
    };

    private AiError MapFailure(CloudHttpFailure failure) => failure.Status switch
    {
        401 => AiErrors.InvalidKey(DisplayName, failure.Status),
        403 => AiErrors.NotPermitted(DisplayName, failure.Status, failure.ErrorType),
        402 => AiErrors.QuotaExhausted(DisplayName, failure.Status, failure.ErrorType),
        413 => AiErrors.ContentTooLong(DisplayName, status: failure.Status),
        429 => AiErrors.RateLimited(DisplayName, failure.RetryAfter, failure.Status, failure.ErrorType),
        400 when failure.ErrorMessage is { } text && text.Contains("too long", StringComparison.OrdinalIgnoreCase) => TooLong(failure, text),
        >= 500 => AiErrors.ServerFailed(DisplayName, failure.Status, failure.ErrorType),
        _ => AiErrors.Rejected(DisplayName, failure.Status, failure.ErrorType),
    };

    private AiError TooLong(CloudHttpFailure failure, string text)
    {
        // "prompt is too long: 1050123 tokens > 1000000 maximum"
        var match = TokensOverLimit().Match(text);
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens)
            && int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit)
            ? AiErrors.ContentTooLong(DisplayName, tokens, limit, failure.Status)
            : AiErrors.ContentTooLong(DisplayName, status: failure.Status);
    }

    [GeneratedRegex(@"(\d+)\s*tokens\s*>\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokensOverLimit();
}
