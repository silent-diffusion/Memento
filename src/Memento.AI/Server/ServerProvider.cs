using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Memento.AI.Http;
using Memento.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Memento.AI.Server;

/// <summary>
/// "Your own server" (2.5.0): any OpenAI-compatible server the user runs (Ollama, LM Studio, vLLM, a llama.cpp server)
/// through <c>POST {address}/v1/chat/completions</c>, always streamed. A JSON answer asks for
/// <c>response_format: json_schema</c> (strict); a server that refuses one with a 400 naming the schema gets the request
/// once more with <c>json_object</c> and the schema in the system message, and is remembered for the process lifetime
/// (<see cref="ServerSchemaSupport"/>), so <see cref="Capabilities"/> then reports no schema support. The bearer key is
/// sent only when one is saved. Requests go through the shared client and <see cref="CloudRequestRunner"/>, so the
/// redirect rule (security audit, SA-36) and the size caps (SA-45: 4 MiB of text, 8 MiB per line or event) hold here
/// as for the clouds. Raw HTTP, like the other providers.
/// </summary>
public sealed partial class ServerProvider : IAiProvider
{
    public const string ProviderId = AiProviders.Server;

    /// <summary>Where requests go below the address.</summary>
    public const string ChatPath = "v1/chat/completions";

    private const int MinAnswerTokens = 256;

    // The error types the stream mapping knows, from what a stream event or an answer document said.
    private const string NotCompatibleType = "not_openai_compatible";
    private const string ContextType = "context_length_exceeded";
    private const string ModelType = "model_not_found";

    private static readonly string[] ContextWords =
    [
        "context length", "context_length", "context size", "context window", "maximum context", "exceed_context", "exceeds the available context",
        "context the overflows", "context overflow", "too many tokens", "prompt is too long", "input is too long", "n_ctx",
    ];

    private readonly ISecretReader _secrets;
    private readonly ServerOptions _options;
    private readonly CloudRequestRunner _runner;
    private readonly ILogger<ServerProvider> _logger;
    private readonly Uri _chatUrl;
    private readonly string _address;

    public ServerProvider(HttpClient http, ISecretReader secrets, ServerOptions options, ILogger<ServerProvider> logger, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        _secrets = secrets;
        _options = options;
        _logger = logger;
        _address = options.Address.Text;
        _chatUrl = options.Address.Resolve(ChatPath);
        _runner = new CloudRequestRunner(http, options.Http, time ?? TimeProvider.System, EstimatingTokenCounter.Server, logger);
    }

    public string Id => ProviderId;

    public string DisplayName => AiProviders.DisplayName(AiProviders.Server);

    public AiProviderKind Kind => AiProviderKind.Server;

    public string Model => _options.Model;

    /// <summary>The address requests go to (<c>http://localhost:11434</c>).</summary>
    public string Address => _address;

    /// <summary>Whether a request takes the inputs off this PC (every address but a loopback one).</summary>
    public bool LeavesPc => _options.Address.LeavesPc;

    /// <summary>
    /// The context from Settings, half of it at most for the answer, and JSON schema support until the server refuses a
    /// schema (then <c>false</c> for as long as Memento runs).
    /// </summary>
    public AiCapabilities Capabilities => new(
        _options.ContextTokens,
        Math.Max(MinAnswerTokens, _options.ContextTokens / 2),
        SupportsJsonSchema: ServerSchemaSupport.Supports(_address),
        SupportsGrammar: false,
        SupportsStreaming: true,
        ExactTokenCounts: false);

    /// <summary>The sentences name the server by its address.</summary>
    private string Named => "The server at " + _address;

    public int CountTokens(string text) => EstimatingTokenCounter.Server.Count(text);

    public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_options.Address.IsInsecure
            ? AiReadiness.NotReady(AiErrors.ServerInsecure(DisplayName, _address))
            : AiReadiness.Ready(string.Create(CultureInfo.InvariantCulture, $"{_options.Model} at {_options.Address.HostAndPort}")));

    public async Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (request.Grammar is not null && request.JsonSchema is null)
        {
            throw new ArgumentException("An OpenAI-compatible server constrains output with a JSON schema, not a GBNF grammar; set JsonSchema.", nameof(request));
        }

        if (_options.Address.IsInsecure)
        {
            // Settings refuses to make this provider ready; this is the last line, so the key and the text never go out in clear.
            throw new AiException(AiErrors.ServerInsecure(DisplayName, _address));
        }

        var key = _secrets.HasKey(ProviderId) ? await _secrets.GetKeyAsync(ProviderId, cancellationToken) : null;
        var mode = request.JsonSchema is null ? SchemaMode.None : ServerSchemaSupport.Supports(_address) ? SchemaMode.Strict : SchemaMode.JsonObject;
        CloudOutcome outcome;
        try
        {
            outcome = await SendAsync(request, key, mode, progress, cancellationToken);
        }
        catch (SchemaRefusedException)
        {
            ServerSchemaSupport.MarkRefused(_address);
            LogSchemaRefused(_address);
            outcome = await SendAsync(request, key, SchemaMode.JsonObject, progress, cancellationToken);
        }

        var result = outcome.Result;
        return new AiResponse(
            Id,
            result.Model ?? Model,
            result.Text,
            ParseAnswer(request, result),
            result.StopReason,
            result.ProviderStopReason,
            result.Usage,
            outcome.Timings,
            AiRequestHash.Compute(request, Id, Model));
    }

    internal enum SchemaMode
    {
        None,
        Strict,
        JsonObject,
    }

    internal byte[] BuildBody(AiRequest request, SchemaMode mode)
    {
        var system = request.System;
        if (mode == SchemaMode.JsonObject && request.JsonSchema is { } constraint)
        {
            var rule = "Answer with one JSON object that matches this JSON Schema, and nothing else (no code fences, no comments):\n" + constraint.GetRawText();
            system = string.IsNullOrEmpty(system) ? rule : system + "\n\n" + rule;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _options.Model);
            writer.WriteBoolean("stream", true);
            writer.WriteNumber("max_tokens", AnswerTokens(request, system));
            if (request.Temperature is { } temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            writer.WriteStartArray("messages");
            if (!string.IsNullOrEmpty(system))
            {
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", system);
                writer.WriteEndObject();
            }

            foreach (var message in request.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.Role == AiRole.User ? "user" : "assistant");
                writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (mode == SchemaMode.Strict && request.JsonSchema is { } schema)
            {
                writer.WriteStartObject("response_format");
                writer.WriteString("type", "json_schema");
                writer.WriteStartObject("json_schema");
                writer.WriteString("name", OpenAI.OpenAiProvider.SchemaName(request.SchemaName));
                writer.WritePropertyName("schema");
                schema.WriteTo(writer);
                writer.WriteBoolean("strict", true);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            else if (mode == SchemaMode.JsonObject)
            {
                writer.WriteStartObject("response_format");
                writer.WriteString("type", "json_object");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The answer limit: what the request asks for, but never more than the context leaves after the prompt (estimated
    /// high), because vLLM refuses a request whose prompt and limit together pass the context.
    /// </summary>
    private int AnswerTokens(AiRequest request, string system)
    {
        var prompt = CountTokens(system) + request.Messages.Sum(m => CountTokens(m.Content) + 8);
        var room = _options.ContextTokens - prompt - 64;
        return Math.Max(MinAnswerTokens, Math.Min(request.MaxOutputTokens, room));
    }

    /// <summary>
    /// The answer as JSON when the request asked for it and it completed: the text itself, or (after a <c>json_object</c>
    /// fallback, from models that wrap their answer) the text without a leading <c>&lt;think&gt;</c> block or code fence,
    /// or the outermost object. Anything else is unreadable; the pipeline's per-module validation checks the shape.
    /// </summary>
    internal JsonElement? ParseAnswer(AiRequest request, CloudStreamResult result)
    {
        if (!request.ExpectsJson || result.StopReason != AiStopReason.Completed)
        {
            return null;
        }

        return CloudJson.ParseJsonAnswer(result.Text)
            ?? CloudJson.ParseJsonAnswer(Unwrap(result.Text))
            ?? throw new AiException(AiErrors.Unreadable(Named, "the answer is not valid JSON"));
    }

    internal static string Unwrap(string text)
    {
        var value = ThinkBlock().Replace(text, string.Empty).Trim();
        if (Fence().Match(value) is { Success: true } fenced)
        {
            value = fenced.Groups[1].Value.Trim();
        }

        var start = value.IndexOf('{', StringComparison.Ordinal);
        var end = value.LastIndexOf('}');
        return start >= 0 && end > start ? value[start..(end + 1)] : value;
    }

    private Task<CloudOutcome> SendAsync(AiRequest request, string? key, SchemaMode mode, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        var body = BuildBody(request, mode);
        var call = new CloudCall(
            Named,
            request.Purpose,
            () => CreateMessage(body, key),
            ReadStreamAsync,
            failure => MapFailure(failure, mode, key is not null),
            MapStreamError,
            MapNetwork,
            after => AiErrors.ServerTimedOut(DisplayName, _address, after));
        return _runner.RunAsync(call, progress, cancellationToken);
    }

    private HttpRequestMessage CreateMessage(byte[] body, string? key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _chatUrl)
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (!string.IsNullOrWhiteSpace(key))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    /// <summary>
    /// Server-sent chat completion chunks until <c>[DONE]</c> (or the end of the stream after a <c>finish_reason</c>),
    /// with a final <c>usage</c> chunk when the server sends one. A server that answers with one JSON document instead
    /// (a whole completion, or an error: LM Studio does on some errors) is read as that document.
    /// </summary>
    private async Task<CloudStreamResult> ReadStreamAsync(Stream stream, CloudStreamContext context, CancellationToken cancellationToken)
    {
        // An event stream starts with "data:", "event:" or a ":" comment, never with "{": whatever the media type says,
        // a body that starts with an object is one JSON document.
        var head = new byte[1024];
        int read;
        using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            idle.CancelAfter(_options.Http.StreamIdleTimeout);
            try
            {
                read = await stream.ReadAtLeastAsync(head, 1, throwOnEndOfStream: false, idle.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The server sent nothing after its headers.");
            }
        }

        var start = head.AsSpan(0, read).IndexOfAnyExcept(" \t\r\n"u8);
        var body = new PrefixedStream(head[..read], stream);
        if (start >= 0 && head[start] is (byte)'{' or (byte)'[')
        {
            return await ReadDocumentAsync(body, context, cancellationToken);
        }

        string? model = null, finish = null;
        var usage = AiUsage.None;
        var done = false;
        await foreach (var item in SseReader.ReadAsync(body, _options.Http.StreamIdleTimeout, cancellationToken))
        {
            if (item.Data.Trim() == "[DONE]")
            {
                done = true;
                break;
            }

            using var document = JsonDocument.Parse(item.Data);
            var root = document.RootElement;
            ThrowIfError(root);
            model ??= CloudJson.String(root, "model");
            usage = ReadUsage(root) ?? usage;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("delta", out var delta))
                {
                    // Only the answer: reasoning that some servers stream beside it ("reasoning", "reasoning_content") is not part of it.
                    context.Append(CloudJson.String(delta, "content"));
                }

                finish = CloudJson.String(choice, "finish_reason") ?? finish;
            }
        }

        if (!done && finish is null)
        {
            throw new CloudStreamException("stream ended early", retryable: false);
        }

        return new CloudStreamResult(context.Text, model, Stop(finish), finish, usage);
    }

    private async Task<CloudStreamResult> ReadDocumentAsync(Stream body, CloudStreamContext context, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
        var text = new StringBuilder();
        var chunk = new char[8192];
        int n;
        while ((n = await reader.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
        {
            if (text.Length + n > SseReader.MaxEventChars)
            {
                throw new CloudAnswerTooLongException();
            }

            text.Append(chunk, 0, n);
        }

        using var document = JsonDocument.Parse(text.ToString());
        var root = document.RootElement;
        ThrowIfError(root);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0
            || choices[0].ValueKind != JsonValueKind.Object)
        {
            throw new CloudStreamException(NotCompatibleType, retryable: false);
        }

        var choice = choices[0];
        var content = choice.TryGetProperty("message", out var message) ? CloudJson.String(message, "content")
            : choice.TryGetProperty("delta", out var delta) ? CloudJson.String(delta, "content")
            : CloudJson.String(choice, "text");
        context.Append(content);
        var finish = CloudJson.String(choice, "finish_reason");
        return new CloudStreamResult(context.Text, CloudJson.String(root, "model"), Stop(finish ?? "stop"), finish, ReadUsage(root) ?? AiUsage.None);
    }

    /// <summary>An <c>error</c> member in a stream event or the answer document: thrown with a type the stream mapping knows.</summary>
    private void ThrowIfError(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error) || error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        var text = error.ValueKind == JsonValueKind.String ? error.GetString() : CloudJson.String(error, "message");
        var type = error.ValueKind == JsonValueKind.Object ? CloudJson.String(error, "code") ?? CloudJson.String(error, "type") : null;
        if (MentionsContext(text) || MentionsContext(type))
        {
            type = ContextType;
        }
        else if (text is not null && text.Contains(Model, StringComparison.Ordinal) && (text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            type = ModelType;
        }

        throw new CloudStreamException(type ?? "error", retryable: type is "server_error" or "overloaded_error" or "rate_limit_exceeded");
    }

    private static AiUsage? ReadUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var cached = usage.TryGetProperty("prompt_tokens_details", out var details) ? CloudJson.Int(details, "cached_tokens") : null;
        return new AiUsage(CloudJson.Int(usage, "prompt_tokens") ?? 0, CloudJson.Int(usage, "completion_tokens") ?? 0, cached);
    }

    private static AiStopReason Stop(string? finish) => finish switch
    {
        "stop" or "eos" or "end_turn" => AiStopReason.Completed,
        "length" or "max_tokens" => AiStopReason.MaxTokens,
        "content_filter" => AiStopReason.Refusal,
        _ => AiStopReason.Other,
    };

    private AiError MapFailure(CloudHttpFailure failure, SchemaMode mode, bool hasKey)
    {
        if (mode == SchemaMode.Strict && failure.Status is 400 or 422 && MentionsSchema(failure))
        {
            throw new SchemaRefusedException();
        }

        var type = failure.ErrorCode ?? failure.ErrorType;
        return failure.Status switch
        {
            401 or 403 => AiErrors.ServerNeedsKey(DisplayName, _address, failure.Status, hasKey),

            // Never another model in its place: the person chooses one (DESIGN.md §17).
            400 or 404 when failure.IsUnknownModel(Model) => AiErrors.ServerModelUnknown(DisplayName, _address, Model, failure.Status),
            413 => AiErrors.ServerContentTooLong(DisplayName, _address, Model, failure.Status),
            400 or 422 or 500 when MentionsContext(failure.ErrorMessage) || MentionsContext(type) => TooLong(failure),
            404 or 405 or 501 => AiErrors.ServerNotCompatible(DisplayName, _address, failure.Status, "/" + ChatPath),
            429 => AiErrors.RateLimited(Named, failure.RetryAfter, failure.Status, type),
            >= 500 => AiErrors.ServerError(DisplayName, _address, failure.Status, type),
            _ => AiErrors.ServerRejected(DisplayName, _address, Model, failure.Status, type),
        };
    }

    private AiError MapStreamError(string? type) => type switch
    {
        ContextType => AiErrors.ServerContentTooLong(DisplayName, _address, Model, null),
        ModelType => AiErrors.ServerModelUnknown(DisplayName, _address, Model, null),
        NotCompatibleType => AiErrors.ServerNotCompatible(DisplayName, _address, null, "/" + ChatPath),
        "rate_limit_exceeded" => AiErrors.RateLimited(Named, null, null, type),
        _ => AiErrors.StreamFailed(Named, type),
    };

    private AiError MapNetwork(HttpRequestException exception)
    {
        var socket = exception.InnerException as SocketException ?? exception.InnerException?.InnerException as SocketException;
        var refused = socket?.SocketErrorCode == SocketError.ConnectionRefused;
        var diagnostic = socket is null ? exception.HttpRequestError.ToString() : socket.SocketErrorCode.ToString();
        return AiErrors.ServerUnreachable(DisplayName, _address, refused, diagnostic);
    }

    private AiError TooLong(CloudHttpFailure failure)
    {
        // vLLM: "This model's maximum context length is 8192 tokens. However, you requested 9000 tokens (…)."
        var message = failure.ErrorMessage ?? string.Empty;
        var limit = MaximumContext().Match(message);
        var actual = Requested().Match(message);
        return limit.Success && actual.Success
            && int.TryParse(limit.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)
            && int.TryParse(actual.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens)
            ? AiErrors.ServerContentTooLong(DisplayName, _address, Model, failure.Status, tokens, max)
            : AiErrors.ServerContentTooLong(DisplayName, _address, Model, failure.Status);
    }

    private static bool MentionsSchema(CloudHttpFailure failure) =>
        new[] { failure.ErrorMessage, failure.ErrorType, failure.ErrorCode }.Any(t =>
            t is not null && (t.Contains("response_format", StringComparison.OrdinalIgnoreCase) || t.Contains("schema", StringComparison.OrdinalIgnoreCase)));

    private static bool MentionsContext(string? text) =>
        text is not null && ContextWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"maximum context length is (\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MaximumContext();

    [GeneratedRegex(@"(?:requested|resulted in) (\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Requested();

    [GeneratedRegex(@"^\s*<think>.*?</think>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^```[a-zA-Z]*\s*\n(.*?)\n?```\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Fence();

    [LoggerMessage(Level = LogLevel.Information, Message = "Your own server at {Address} refused a JSON schema; asking it for a JSON object from now on")]
    private partial void LogSchemaRefused(string address);
}
