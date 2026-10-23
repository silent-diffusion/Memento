using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Memento.Core.Ai;

/// <summary>
/// The address of "Your own server" (Ollama, LM Studio, vLLM, a llama.cpp server or any OpenAI-compatible endpoint),
/// parsed and classified once so Settings, readiness, consent and the provider agree on it. An address is an absolute
/// <c>http</c> or <c>https</c> URL without a query, fragment or user name (a password in a URL would be stored in plain
/// text); a missing scheme reads as <c>http://</c>, and a trailing <c>/v1</c> (how OpenAI-compatible servers document
/// their base URL) is dropped, because Memento adds <c>/v1/chat/completions</c> and <c>/v1/models</c> itself. Where it
/// runs (<see cref="Zone"/>) is judged from the text alone, never by a DNS lookup, so <c>providers.list</c> stays silent
/// on the network.
/// </summary>
public sealed record ServerAddress
{
    /// <summary>The longest address Settings accepts.</summary>
    public const int MaxLength = 400;

    private ServerAddress(Uri uri, ServerZone zone)
    {
        Uri = uri;
        Zone = zone;
    }

    /// <summary>The base address, ending with a slash (<c>http://localhost:11434/</c>); request paths are relative to it.</summary>
    public Uri Uri { get; }

    public ServerZone Zone { get; }

    /// <summary>The address as Settings stores and shows it: <c>http://localhost:11434</c>, without a trailing slash.</summary>
    public string Text => Uri.AbsoluteUri.TrimEnd('/');

    /// <summary>The host and port, as the model label names it: <c>localhost:11434</c>.</summary>
    public string HostAndPort => Uri.IsDefaultPort ? Uri.Host : string.Create(CultureInfo.InvariantCulture, $"{Uri.Host}:{Uri.Port}");

    public bool IsLoopback => Zone == ServerZone.ThisPc;

    /// <summary>The inputs leave this PC when they are sent there (every zone but <see cref="ServerZone.ThisPc"/>).</summary>
    public bool LeavesPc => Zone != ServerZone.ThisPc;

    /// <summary>Plain <c>http://</c> to an address outside this PC and the local network: refused (<c>ai.serverInsecure</c>).</summary>
    public bool IsInsecure => Zone == ServerZone.Remote && Uri.Scheme == Uri.UriSchemeHttp;

    /// <summary>"this PC", "on your network", "remote": the words History and "How this was made" add after the model.</summary>
    public string ZoneLabel => Zone switch
    {
        ServerZone.ThisPc => "this PC",
        ServerZone.Network => "on your network",
        _ => "remote",
    };

    /// <summary>A request path below the base address (<c>v1/chat/completions</c>).</summary>
    public Uri Resolve(string relativePath) => new(Uri, relativePath);

    /// <summary>The address in <paramref name="text"/>, or <c>null</c> with the reason in <paramref name="problem"/>.</summary>
    public static ServerAddress? Parse(string? text, out string? problem)
    {
        problem = null;
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            problem = "it is empty";
            return null;
        }

        if (value.Length > MaxLength)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"it is longer than {MaxLength} characters");
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            problem = "it is not an http:// or https:// address";
            return null;
        }

        if (uri.UserInfo.Length > 0)
        {
            problem = "it contains a user name or password (save the server's key instead)";
            return null;
        }

        if (uri.Query.Length > 0 || uri.Fragment.Length > 0 || value.Contains('?', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal))
        {
            problem = "it has a query or a fragment (a part after ? or #)";
            return null;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            problem = "it has no host name";
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^3];
        }

        var normalized = new UriBuilder(uri.Scheme, uri.Host, uri.Port, path + "/").Uri;
        return new ServerAddress(normalized, Classify(normalized));
    }

    /// <summary>Where an address points, from its text: loopback, the local network, or anywhere else.</summary>
    public static ServerZone Classify(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return ServerZone.ThisPc;
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return ServerZone.ThisPc;
            }

            return IsPrivate(address) ? ServerZone.Network : ServerZone.Remote;
        }

        // A name without a dot (a computer on the network, "gpu-box") or an mDNS ".local" name stays on the local network.
        return !host.Contains('.', StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            ? ServerZone.Network
            : ServerZone.Remote;
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 10
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 && (address.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC);
    }
}
