using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Memento.AI.Tests.Fakes;

/// <summary>
/// A fake provider endpoint on 127.0.0.1: answers each request with the next scripted response (status, headers, a
/// body written in pieces with optional pauses, or a stall), and records every request it read.
/// </summary>
internal sealed class ScriptedHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<ScriptedResponse> _script = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly Task _loop;

    public ScriptedHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    public Uri BaseUrl => new(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Port}/"));

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

    public ScriptedHttpServer Enqueue(ScriptedResponse response)
    {
        _script.Enqueue(response);
        return this;
    }

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

                var response = _script.TryDequeue(out var next) ? next : ScriptedResponse.Json(404, "{}");
                if (response.StallBeforeHeaders)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }

                var head = new StringBuilder()
                    .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {response.Status} Scripted\r\n")
                    .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
                    .Append("Connection: close\r\n");
                foreach (var (name, value) in response.Headers)
                {
                    head.Append(name).Append(": ").Append(value).Append("\r\n");
                }

                head.Append("\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _stop.Token);
                await stream.FlushAsync(_stop.Token);
                foreach (var part in response.Parts)
                {
                    if (part.Delay > TimeSpan.Zero)
                    {
                        await Task.Delay(part.Delay, _stop.Token);
                    }

                    await stream.WriteAsync(Encoding.UTF8.GetBytes(part.Text), _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                }

                if (response.StallAfterBody)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away or the server is stopping.
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
