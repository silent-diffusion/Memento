using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Memento.Core.Tests.Fakes;

/// <summary>
/// A tiny HTTP/1.1 file server on 127.0.0.1 for model-manager tests: serves byte arrays by path, honours
/// <c>Range: bytes=n-</c> with 206, can throttle, stall or cut a response short, and records the requests it saw.
/// </summary>
internal sealed class LocalHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly List<string> _requests = [];
    private readonly Task _loop;

    public LocalHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>Bytes sent per write; with <see cref="ChunkDelay"/> this slows a download down.</summary>
    public int ChunkSize { get; set; } = 64 * 1024;

    public TimeSpan ChunkDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Close the connection after this many body bytes (a dropped connection).</summary>
    public long? CutAfterBytes { get; set; }

    /// <summary>Answer every request with this status and no body.</summary>
    public int? FailWithStatus { get; set; }

    /// <summary>Claim this start in the <c>Content-Range</c> of a 206 (a server answering another piece than asked).</summary>
    public long? ContentRangeStart { get; set; }

    /// <summary>Paths answered with <c>302 Found</c> and this <c>Location</c>.</summary>
    public Dictionary<string, string> Redirects { get; } = new(StringComparer.Ordinal);

    public List<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public string Url(string path) => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Port}/{path}");

    public void Add(string path, byte[] content) => _files[path] = content;

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
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                if (requestLine is null)
                {
                    return;
                }

                long? from = null;
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
                {
                    if (header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                    {
                        from = long.Parse(header["Range: bytes=".Length..].TrimEnd('-'), CultureInfo.InvariantCulture);
                    }
                }

                lock (_requests)
                {
                    _requests.Add(requestLine + (from is { } f ? string.Create(CultureInfo.InvariantCulture, $" range {f}-") : string.Empty));
                }

                var path = requestLine.Split(' ')[1].TrimStart('/');
                if (Redirects.TryGetValue(path, out var location))
                {
                    await WriteHeadAsync(stream, 302, 0, null, location);
                    return;
                }

                if (FailWithStatus is { } status || !_files.TryGetValue(path, out var content))
                {
                    await WriteHeadAsync(stream, FailWithStatus ?? 404, 0, null);
                    return;
                }

                var start = from is { } s && s < content.Length ? s : 0;
                if (from is { } past && past >= content.Length)
                {
                    await WriteHeadAsync(stream, 416, 0, null);
                    return;
                }

                await WriteHeadAsync(stream, start > 0 ? 206 : 200, content.Length - start, start > 0 ? string.Create(CultureInfo.InvariantCulture, $"bytes {ContentRangeStart ?? start}-{content.Length - 1}/{content.Length}") : null);
                long sent = 0;
                for (var offset = start; offset < content.Length; offset += ChunkSize)
                {
                    var count = (int)Math.Min(ChunkSize, content.Length - offset);
                    if (CutAfterBytes is { } cut && sent + count > cut)
                    {
                        count = (int)Math.Max(0, cut - sent);
                        await stream.WriteAsync(content.AsMemory((int)offset, count), _stop.Token);
                        return;
                    }

                    await stream.WriteAsync(content.AsMemory((int)offset, count), _stop.Token);
                    sent += count;
                    if (ChunkDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(ChunkDelay, _stop.Token);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away or the server is stopping.
            }
        }
    }

    private static async Task WriteHeadAsync(NetworkStream stream, int status, long length, string? contentRange, string? location = null)
    {
        var head = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {(status is 200 or 206 ? "OK" : status == 302 ? "Found" : "Error")}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Length: {length}\r\n")
            .Append("Content-Type: application/octet-stream\r\nConnection: close\r\n");
        if (contentRange is not null)
        {
            head.Append("Content-Range: ").Append(contentRange).Append("\r\n");
        }

        if (location is not null)
        {
            head.Append("Location: ").Append(location).Append("\r\n");
        }

        head.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
    }
}
