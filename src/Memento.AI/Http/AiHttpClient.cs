using System.Net;

namespace Memento.AI.Http;

/// <summary>
/// The one <see cref="HttpClient"/> the cloud providers share: TLS 1.2+ (the system default), pooled connections
/// recycled every five minutes so DNS changes are picked up, no client-wide timeout (the providers time each phase
/// themselves), no cookies, no redirects (.NET drops <c>Authorization</c> on a redirect but not <c>x-api-key</c>, and a
/// 307/308 would resend the transcript), no proxy credentials beyond the system's, and no request logging. Together
/// with <c>ModelDownloadClient</c> it is the only place Memento creates an HTTP client.
/// </summary>
public sealed class AiHttpClient : IDisposable
{
    public AiHttpClient()
        : this(CreateHandler())
    {
    }

    /// <summary>
    /// A client over <paramref name="handler"/> (a test seam: a recording or refusing handler). The handler's own
    /// redirect setting applies; the request runner refuses any 3xx it is handed.
    /// </summary>
    public AiHttpClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Memento/" + (typeof(AiHttpClient).Assembly.GetName().Version?.ToString(3) ?? "0"));
    }

    public HttpClient Client { get; }

    public void Dispose() => Client.Dispose();

    private static SocketsHttpHandler CreateHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        UseCookies = false,
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(20),
    };
}
