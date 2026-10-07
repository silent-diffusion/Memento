using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Memento.Core.Tests.Fakes;

/// <summary>
/// Records every outgoing HTTP request (HttpClient's diagnostic events) and every socket connect (the
/// <c>System.Net.Sockets</c> event source) made anywhere in this process while it is alive. Used by the network
/// verification tests of the security audit: managed code cannot reach the network without one of the two showing.
/// </summary>
internal sealed class NetworkSpy : EventListener, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private readonly ConcurrentQueue<string> _events = new();
    private readonly List<IDisposable> _subscriptions = [];
    private volatile bool _listening;

    public NetworkSpy()
    {
        _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        _listening = true;
    }

    /// <summary><c>http GET https://host/path</c> and <c>connect 203.0.113.9:443</c>, in order.</summary>
    public IReadOnlyList<string> Events => [.. _events];

    public override void Dispose()
    {
        _listening = false;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        base.Dispose();
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
    {
        if (value.Name == "HttpHandlerDiagnosticListener")
        {
            lock (_subscriptions)
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (!_listening || value.Key != "System.Net.Http.Request")
        {
            return;
        }

        var request = value.Value?.GetType().GetProperty("Request")?.GetValue(value.Value) as HttpRequestMessage;
        _events.Enqueue($"http {request?.Method} {request?.RequestUri}");
    }

    void IObserver<DiagnosticListener>.OnCompleted()
    {
    }

    void IObserver<DiagnosticListener>.OnError(Exception error)
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted()
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
    {
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "System.Net.Sockets")
        {
            EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (_listening && eventData.EventName == "ConnectStart")
        {
            _events.Enqueue("connect " + (eventData.Payload is { Count: > 0 } payload ? Endpoint(payload[0]?.ToString()) : "?"));
        }
    }

    /// <summary><c>InterNetworkV6:28:{b0,b1,…}</c> (a SocketAddress) as <c>[address]:port</c>.</summary>
    private static string Endpoint(string? socketAddress)
    {
        var open = socketAddress?.IndexOf('{', StringComparison.Ordinal) ?? -1;
        if (socketAddress is null || open < 0)
        {
            return socketAddress ?? "?";
        }

        var bytes = socketAddress[(open + 1)..].TrimEnd('}').Split(',').Select(b => byte.Parse(b, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        // The braces list the address bytes after the two-byte family: port (big-endian), then for IPv6 the flow
        // information and the 16 address bytes, for IPv4 the 4 address bytes.
        var port = (bytes[0] << 8) | bytes[1];
        System.Net.IPAddress address = socketAddress.StartsWith("InterNetworkV6", StringComparison.Ordinal) && bytes.Length >= 22
            ? new System.Net.IPAddress(bytes[6..22])
            : new System.Net.IPAddress(bytes[2..6]);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return new System.Net.IPEndPoint(address, port).ToString();
    }
}
