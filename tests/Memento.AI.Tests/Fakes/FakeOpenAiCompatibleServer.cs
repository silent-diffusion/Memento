using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Memento.AI.Tests.Fakes;

/// <summary>
/// A fake "Your own server" on 127.0.0.1 that answers like Ollama, LM Studio or vLLM (<see cref="ServerPersonality"/>):
/// <c>GET /v1/models</c>, <c>GET /api/version</c> (Ollama only) and streamed <c>POST /v1/chat/completions</c>, with the
/// answer text from <see cref="Answer"/>, a key check when <see cref="RequiredKey"/> is set, and a 404 for a model it
/// does not list. <see cref="Override"/> replaces the answer to any request (a redirect, a 5 MiB answer, a page that is
/// not JSON). Every request is recorded; every answer closes its connection. Never a real server and never a real API.
/// </summary>
internal sealed class FakeOpenAiCompatibleServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly Task _loop;

    public FakeOpenAiCompatibleServer(ServerPersonality personality)
    {
        Personality = personality;
        Models = personality switch
        {
            ServerPersonality.Ollama => ["qwen3:8b", "llama3.2:3b"],
            ServerPersonality.LmStudio => ["qwen/qwen3-8b"],
            _ => ["Qwen/Qwen3-8B"],
        };
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptAsync);
    }

    public ServerPersonality Personality { get; }

    public int Port { get; }

    /// <summary><c>http://127.0.0.1:{port}</c>, as Settings stores it.</summary>
    public string Address => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Port}");

    /// <summary>The models <c>/v1/models</c> lists and chat completions accept.</summary>
    public List<string> Models { get; }

    /// <summary>When set, a request without <c>Authorization: Bearer {key}</c> is answered 401.</summary>
    public string? RequiredKey { get; set; }

    /// <summary>The answer text for a chat completion request (its JSON body); a JSON object by default.</summary>
    public Func<JsonElement, string> Answer { get; set; } = _ => """{"items":["a","b"]}""";

    /// <summary>Answers a request in place of the personality (return <c>null</c> to let it answer).</summary>
    public Func<RecordedRequest, ScriptedResponse?>? Override { get; set; }

    /// <summary>End the stream with <c>data: [DONE]</c> (every personality does; off shows the end of the stream is enough).</summary>
    public bool SendDone { get; set; } = true;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public IReadOnlyList<RecordedRequest> Completions => Requests.Where(r => r.Path == "/v1/chat/completions").ToList();

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Stopped.
        }

        _stop.Dispose();
    }

    /// <summary>A port nothing listens on (taken, then released): "nothing is listening" without guessing.</summary>
    public static int ClosedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>A chat completion stream of <paramref name="text"/> in a few pieces, as <see cref="Personality"/> sends it.</summary>
    public ScriptedResponse Stream(string text, string model, int pieces = 3)
    {
        var events = new List<string>();
        var size = Math.Max(1, (int)Math.Ceiling(text.Length / (double)Math.Max(1, pieces)));
        for (var i = 0; i < text.Length; i += size)
        {
            events.Add(Chunk(model, new JsonObject { ["role"] = "assistant", ["content"] = text.Substring(i, Math.Min(size, text.Length - i)) }, null));
        }

        events.Add(Chunk(model, new JsonObject(), "stop"));
        if (Personality == ServerPersonality.LmStudio)
        {
            events.Add(new JsonObject
            {
                ["id"] = "chatcmpl-test",
                ["object"] = "chat.completion.chunk",
                ["model"] = model,
                ["choices"] = new JsonArray(),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 120, ["completion_tokens"] = 7, ["total_tokens"] = 127 },
            }.ToJsonString());
        }

        if (SendDone)
        {
            events.Add("[DONE]");
        }

        return new ScriptedResponse
        {
            ContentType = "text/event-stream",
            Headers = Headers(),
            Parts = events.Select(e => ("data: " + e + "\n\n", TimeSpan.Zero)).ToList(),
        };
    }

    public static string Chunk(string model, JsonObject delta, string? finish) => new JsonObject
    {
        ["id"] = "chatcmpl-test",
        ["object"] = "chat.completion.chunk",
        ["created"] = 1_760_000_000,
        ["model"] = model,
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }),
    }.ToJsonString();

    private (string Name, string Value)[] Headers() => Personality == ServerPersonality.Vllm ? [("Server", "uvicorn")] : [];

    private ScriptedResponse Respond(RecordedRequest request)
    {
        if (Override?.Invoke(request) is { } scripted)
        {
            return scripted;
        }

        if (RequiredKey is { } key && request.Header("Authorization") != "Bearer " + key)
        {
            return ScriptedResponse.Json(401, """{"error":{"message":"Invalid API key","type":"invalid_request_error","param":null,"code":"invalid_api_key"}}""");
        }

        switch (request.Method, request.Path)
        {
            case ("GET", "/v1/models"):
                var owner = Personality switch { ServerPersonality.Ollama => "library", ServerPersonality.LmStudio => "organization_owner", _ => "vllm" };
                var data = new JsonArray(Models.Select(m => (JsonNode)new JsonObject { ["id"] = m, ["object"] = "model", ["created"] = 1_760_000_000, ["owned_by"] = owner }).ToArray());
                return ScriptedResponse.Json(200, new JsonObject { ["object"] = "list", ["data"] = data }.ToJsonString(), Headers());
            case ("GET", "/api/version") when Personality == ServerPersonality.Ollama:
                return ScriptedResponse.Json(200, """{"version":"0.12.3"}""");
            case ("POST", "/v1/chat/completions"):
                return Complete(request);
            default:
                return Personality == ServerPersonality.Ollama
                    ? new ScriptedResponse { Status = 404, ContentType = "text/plain", Parts = [("404 page not found", TimeSpan.Zero)] }
                    : ScriptedResponse.Json(404, """{"error":"Unexpected endpoint or method."}""", Headers());
        }
    }

    private ScriptedResponse Complete(RecordedRequest request)
    {
        var body = request.Json;
        var model = body.GetProperty("model").GetString()!;
        if (!Models.Contains(model, StringComparer.Ordinal))
        {
            return Personality switch
            {
                ServerPersonality.Ollama => ScriptedResponse.Json(404, $$$"""{"error":{"message":"model \"{{{model}}}\" not found, try pulling it first","type":"not_found_error","param":null,"code":null}}"""),
                ServerPersonality.LmStudio => ScriptedResponse.Json(404, $$$"""{"error":{"message":"Model '{{{model}}}' not found. Load it in LM Studio first.","type":"invalid_request_error","param":"model","code":"model_not_found"}}"""),
                _ => ScriptedResponse.Json(404, $$$"""{"object":"error","message":"The model `{{{model}}}` does not exist.","type":"NotFoundError","param":null,"code":404}""", Headers()),
            };
        }

        if (Personality == ServerPersonality.Vllm
            && body.TryGetProperty("response_format", out var format)
            && format.GetProperty("type").GetString() == "json_schema")
        {
            return ScriptedResponse.Json(400, """{"error":{"code":400,"message":"Unsupported response_format type 'json_schema': this server only accepts response_format of type 'json_object' or 'text'","type":"invalid_request_error"}}""", Headers());
        }

        return Stream(Answer(body), model);
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                if (request is null)
                {
                    return;
                }

                lock (_requests)
                {
                    _requests.Add(request);
                }

                var response = Respond(request);
                if (response.StallBeforeHeaders)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }

                var head = new StringBuilder()
                    .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {response.Status} Fake\r\n")
                    .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
                    .Append("Connection: close\r\n");
                foreach (var (name, value) in response.Headers)
                {
                    head.Append(name).Append(": ").Append(value).Append("\r\n");
                }

                head.Append("\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _stop.Token);
                foreach (var part in response.Parts)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(part.Text), _stop.Token);
                }

                await stream.FlushAsync(_stop.Token);
                if (response.StallAfterBody)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away (a capped answer is cut short on purpose) or the server is stopping.
            }
        }
    }

    private static async Task<RecordedRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var headerBytes = new List<byte>();
        var buffer = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(buffer) == 0)
            {
                return null;
            }

            headerBytes.Add(buffer[0]);
            var n = headerBytes.Count;
            if (n >= 4 && headerBytes[n - 4] == '\r' && headerBytes[n - 3] == '\n' && headerBytes[n - 2] == '\r' && headerBytes[n - 1] == '\n')
            {
                break;
            }
        }

        var lines = Encoding.ASCII.GetString([.. headerBytes]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var parts = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var length = headers.TryGetValue("Content-Length", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : 0;
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(body.AsMemory(read));
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return new RecordedRequest(parts[0], parts[1], headers, Encoding.UTF8.GetString(body, 0, read));
    }
}
