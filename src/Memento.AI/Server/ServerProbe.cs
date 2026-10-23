using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Memento.Core.Ai;
using Microsoft.Extensions.Logging;

namespace Memento.AI.Server;

/// <summary>
/// "Test connection" for "Your own server" (2.5.0, <c>ai.serverProbe</c>): <c>GET {address}/v1/models</c> with the saved
/// key, if any, and for an Ollama server <c>GET {address}/api/version</c> for its version. Nothing else is sent (no
/// recording content, no prompt), no redirect is followed, each answer is read up to 1 MiB, and each request has
/// <see cref="Timeout"/> on the clock it is given. It runs only when the page asks; the caller decides beforehand whether
/// the address may be contacted at all (external AI off, plain http outside the local network).
/// </summary>
public sealed partial class ServerProbe(HttpClient http, TimeProvider time, ILogger<ServerProbe> logger)
{
    /// <summary>The server lists no model (<c>ai.noModel</c>).</summary>
    public const string NoModelsCode = "ai.noModel";

    /// <summary>How long one probe request may take, from sending to the end of its answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const int MaxBodyBytes = 1024 * 1024;

    private const string ModelsPath = "v1/models";

    private readonly ILogger<ServerProbe> _logger = logger;

    public async Task<ServerProbeOutcome> ProbeAsync(ServerAddress address, string? key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        var text = address.Text;
        Answer models;
        try
        {
            models = await GetAsync(address.Resolve(ModelsPath), key, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            var socket = ex.InnerException as SocketException ?? ex.InnerException?.InnerException as SocketException;
            var refused = socket?.SocketErrorCode == SocketError.ConnectionRefused;
            LogUnreachable(text, socket?.SocketErrorCode, ex.HttpRequestError);
            return new ServerProbeOutcome(
                false,
                null,
                [],
                AiErrorCodes.ServerUnreachable,
                refused
                    ? $"{AiErrors.ServerUnreachableSentence(text, true)} Start Ollama or LM Studio, or check the address in Settings › AI and privacy."
                    : $"{AiErrors.ServerUnreachableSentence(text, false)} Check that the computer is on and the server is running, or check the address in Settings › AI and privacy.");
        }
        catch (TimeoutException)
        {
            LogProbe(text, "timeout", 0, 0);
            return new ServerProbeOutcome(false, null, [], AiErrorCodes.ServerUnreachable, $"The server at {text} did not answer within {AiErrors.Duration(Timeout)}. Check that it is running and not busy loading a model, then test again.");
        }

        var host = address.HostAndPort;
        if (models.Status is >= 300 and < 400)
        {
            LogProbe(text, "redirect", models.Status, 0);
            var to = models.Location is { IsAbsoluteUri: true } location ? " to " + location.GetLeftPart(UriPartial.Authority) : string.Empty;
            return new ServerProbeOutcome(true, models.ServerHeader ?? host, [], AiErrorCodes.ServerNotCompatible, $"The server at {text} answered with a redirect{to} ({Http(models.Status)}), and Memento does not follow redirects. Enter the address the server really answers at in Settings › AI and privacy.");
        }

        if (models.Status is 401 or 403)
        {
            LogProbe(text, "needs a key", models.Status, 0);
            return new ServerProbeOutcome(
                true,
                models.ServerHeader ?? host,
                [],
                AiErrorCodes.ServerNeedsKey,
                key is null
                    ? $"The server at {text} asks for a key ({Http(models.Status)}). Add the server's key in Settings › AI and privacy, then test again."
                    : $"The server at {text} did not accept the saved key ({Http(models.Status)}). Replace the server's key in Settings › AI and privacy, then test again.");
        }

        if (models.Status is < 200 or >= 300 || ReadModels(models.Body) is not { } list)
        {
            LogProbe(text, "not compatible", models.Status, 0);
            var what = models.Status is >= 200 and < 300 ? "no model list Memento could read" : Http(models.Status);
            return new ServerProbeOutcome(true, models.ServerHeader ?? host, [], AiErrorCodes.ServerNotCompatible, $"The server at {text} does not answer like an OpenAI-compatible server ({what} at /{ModelsPath}). Check the address in Settings › AI and privacy: Memento adds /{ModelsPath} to it.");
        }

        var name = await NameAsync(address, key, list.Owners, models.ServerHeader, cancellationToken) ?? host;
        var ids = list.Ids.Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ThenBy(i => i, StringComparer.Ordinal).ToList();
        LogProbe(text, "answered", models.Status, ids.Count);
        if (ids.Count == 0)
        {
            return new ServerProbeOutcome(true, name, ids, NoModelsCode, $"{name} answers at {text} but offers no model. Download or load one on the server (for Ollama, run ollama pull with a model name), then test again.");
        }

        var count = ids.Count == 1 ? "1 model" : string.Create(CultureInfo.InvariantCulture, $"{ids.Count} models");
        var where = address.Zone switch
        {
            ServerZone.ThisPc => "It runs on this PC, so nothing leaves it.",
            ServerZone.Network => "It is on your network, so the inputs leave this PC when you generate.",
            _ => "It is outside your network, so the inputs leave this PC when you generate.",
        };
        return new ServerProbeOutcome(true, name, ids, null, $"Connected to {name} at {text}: {count}. {where}");
    }

    /// <summary>Who answered: the model list's <c>owned_by</c> says which server it is (Ollama also gives its version), else the <c>Server</c> header.</summary>
    private async Task<string?> NameAsync(ServerAddress address, string? key, IReadOnlyList<string> owners, string? serverHeader, CancellationToken cancellationToken)
    {
        if (owners.Contains("library", StringComparer.Ordinal) || owners.Contains("ollama", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var version = await GetAsync(address.Resolve("api/version"), key, cancellationToken);
                if (version.Status is >= 200 and < 300 && ReadString(version.Body, "version") is { Length: > 0 and <= 40 } number)
                {
                    return "Ollama " + number;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                // The model list already answered; the version is a nicety.
            }

            return "Ollama";
        }

        if (owners.Contains("organization_owner", StringComparer.Ordinal))
        {
            return "LM Studio";
        }

        if (owners.Contains("vllm", StringComparer.OrdinalIgnoreCase))
        {
            return "vLLM";
        }

        if (owners.Contains("llamacpp", StringComparer.OrdinalIgnoreCase))
        {
            return "llama.cpp server";
        }

        return serverHeader is { Length: > 0 and <= 60 } && !GenericServers.Any(g => serverHeader.StartsWith(g, StringComparison.OrdinalIgnoreCase)) ? serverHeader : null;
    }

    private static readonly string[] GenericServers = ["uvicorn", "nginx", "apache", "caddy", "microsoft-", "kestrel", "cloudflare", "envoy", "gunicorn", "hypercorn"];

    private sealed record Answer(int Status, string? ServerHeader, Uri? Location, byte[] Body);

    private sealed record ModelList(IReadOnlyList<string> Ids, IReadOnlyList<string> Owners);

    /// <summary><c>{"object": "list", "data": [{"id": …, "owned_by": …}]}</c>; Ollama's native <c>{"models": [{"name": …}]}</c> is not it.</summary>
    private static ModelList? ReadModels(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var ids = new List<string>();
            var owners = new List<string>();
            foreach (var item in data.EnumerateArray().Take(1000))
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 and <= 200 } value)
                {
                    ids.Add(value);
                }

                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("owned_by", out var owner) && owner.ValueKind == JsonValueKind.String)
                {
                    owners.Add(owner.GetString()!);
                }
            }

            return new ModelList(ids, owners);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(byte[] body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One GET within <see cref="Timeout"/>, its body read up to <see cref="MaxBodyBytes"/>; a timeout is a <see cref="TimeoutException"/>.</summary>
    private async Task<Answer> GetAsync(Uri url, string? key, CancellationToken cancellationToken)
    {
        using var limit = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var server = response.Headers.Server.Count > 0 ? response.Headers.Server.ToString() : null;
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var buffer = new byte[MaxBodyBytes];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), linked.Token)) > 0)
            {
                length += read;
            }

            return new Answer((int)response.StatusCode, server, response.Headers.Location, buffer[..length]);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The server did not answer in time.");
        }
        catch (IOException ex)
        {
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "The connection was lost.", ex);
        }
    }

    private static string Http(int status) => string.Create(CultureInfo.InvariantCulture, $"HTTP {status}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Server test at {Address}: {Outcome} (HTTP {Status}, {Models} models)")]
    private partial void LogProbe(string address, string outcome, int status, int models);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server test at {Address}: unreachable ({SocketError}, {HttpError})")]
    private partial void LogUnreachable(string address, SocketError? socketError, HttpRequestError httpError);
}
