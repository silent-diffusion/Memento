using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Memento.Core.Bridge.Contracts;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Bridge;

/// <summary>
/// Validates incoming bridge messages, dispatches them to the registered <see cref="IBridgeHandler"/>
/// and turns every outcome, including exceptions, into a <see cref="BridgeResponse"/>.
/// Exception details stay in the log; the UI only ever sees a code, a message and an optional detail.
/// </summary>
public sealed partial class BridgeRouter
{
    /// <summary>Largest message accepted, in UTF-16 characters (1 MiB).</summary>
    public const int MaxMessageLength = 1024 * 1024;

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 32 };

    private readonly Dictionary<string, IBridgeHandler> _handlers;
    private readonly ILogger<BridgeRouter> _logger;

    public BridgeRouter(IEnumerable<IBridgeHandler> handlers, ILogger<BridgeRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _handlers = new Dictionary<string, IBridgeHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (!MethodNamePattern().IsMatch(handler.Name))
            {
                throw new ArgumentException($"Bridge method name '{handler.Name}' is not in area.verb form.", nameof(handlers));
            }

            if (!_handlers.TryAdd(handler.Name, handler))
            {
                throw new ArgumentException($"Bridge method '{handler.Name}' is registered twice.", nameof(handlers));
            }
        }
    }

    public IReadOnlyCollection<string> MethodNames => _handlers.Keys;

    /// <summary>Handles one raw message from the UI and returns the serialized response.</summary>
    public async Task<string> HandleAsync(string message, CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(message, cancellationToken);
        return JsonSerializer.Serialize(response, BridgeJsonContext.Default.BridgeResponse);
    }

    /// <summary>Handles one raw message from the UI and returns the response object.</summary>
    public async Task<BridgeResponse> DispatchAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message))
        {
            return Fail(null, BridgeErrorCodes.InvalidRequest, "The message from the interface was empty.");
        }

        if (message.Length > MaxMessageLength)
        {
            return Fail(
                null,
                BridgeErrorCodes.InvalidRequest,
                "The message from the interface is too large.",
                string.Create(CultureInfo.InvariantCulture, $"{message.Length} characters; the limit is {MaxMessageLength}."));
        }

        BridgeRequest request;
        try
        {
            using var document = JsonDocument.Parse(message, DocumentOptions);
            var (parsed, error) = ParseRequest(document.RootElement);
            if (error is not null)
            {
                return error;
            }

            request = parsed!;
        }
        catch (JsonException ex)
        {
            return Fail(
                null,
                BridgeErrorCodes.InvalidJson,
                "The message from the interface is not valid JSON.",
                string.Create(CultureInfo.InvariantCulture, $"Line {ex.LineNumber}, byte {ex.BytePositionInLine}."));
        }

        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            return Fail(request.Id, BridgeErrorCodes.UnknownMethod, $"The host has no method named '{request.Method}'.");
        }

        var parameters = request.Params ?? EmptyObject();
        try
        {
            var result = await handler.HandleAsync(parameters, cancellationToken);
            LogHandled(request.Method, request.Id);
            return new BridgeResponse(request.Id, result, null);
        }
        catch (BridgeException ex)
        {
            LogRejected(request.Method, request.Id, ex.Code);
            return Fail(request.Id, ex.Code, ex.Message, ex.Detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(request.Id, BridgeErrorCodes.Cancelled, $"'{request.Method}' was cancelled because Memento is closing.");
        }
#pragma warning disable CA1031 // The bridge must answer every request; unexpected failures become a structured error.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var reference = Guid.NewGuid().ToString("N")[..8];
            LogFailed(ex, request.Method, request.Id, reference);
            return Fail(
                request.Id,
                BridgeErrorCodes.Internal,
                $"The host could not complete '{request.Method}'. The error was written to the Memento log.",
                $"Log reference {reference}.");
        }
    }

    private static (BridgeRequest? Request, BridgeResponse? Error) ParseRequest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, Fail(null, BridgeErrorCodes.InvalidRequest, "A bridge message must be a JSON object."));
        }

        long? id = null;
        if (root.TryGetProperty("id", out var idElement)
            && idElement.ValueKind == JsonValueKind.Number
            && idElement.TryGetInt64(out var parsedId)
            && parsedId >= 0)
        {
            id = parsedId;
        }

        if (id is null)
        {
            return (null, Fail(null, BridgeErrorCodes.InvalidRequest, "A bridge request needs a non-negative integer 'id'."));
        }

        if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return (null, Fail(id, BridgeErrorCodes.InvalidRequest, "A bridge request needs a string 'method'."));
        }

        var method = methodElement.GetString()!;
        if (!MethodNamePattern().IsMatch(method))
        {
            return (null, Fail(id, BridgeErrorCodes.InvalidRequest, "A bridge method name must look like 'area.verb'."));
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("id" or "method" or "params"))
            {
                return (null, Fail(id, BridgeErrorCodes.InvalidRequest, $"A bridge request does not take a '{property.Name}' field."));
            }
        }

        JsonElement? parameters = null;
        if (root.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind != JsonValueKind.Null)
        {
            if (paramsElement.ValueKind != JsonValueKind.Object)
            {
                return (null, Fail(id, BridgeErrorCodes.InvalidParams, $"The parameters for '{method}' must be an object."));
            }

            parameters = paramsElement.Clone();
        }

        return (new BridgeRequest(id.Value, method, parameters), null);
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static BridgeResponse Fail(long? id, string code, string message, string? detail = null) =>
        new(id, null, new BridgeError(code, message, detail));

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex MethodNamePattern();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Bridge {Method} #{Id} handled")]
    private partial void LogHandled(string method, long id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bridge {Method} #{Id} rejected with {Code}")]
    private partial void LogRejected(string method, long id, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bridge {Method} #{Id} failed (log reference {Reference})")]
    private partial void LogFailed(Exception exception, string method, long id, string reference);
}
