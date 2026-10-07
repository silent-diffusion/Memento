using System.Net;

namespace Memento.AI.Http;

/// <summary>
/// The one <see cref="HttpClient"/> the cloud providers share: TLS 1.2+ (the system default), pooled connections
/// recycled every five minutes so DNS changes are picked up, no client-wide timeout (the providers time each phase
/// themselves), no cookies, no proxy credentials beyond the system's, and no request logging.
/// </summary>
public sealed class AiHttpClient : IDisposable
{
    public AiHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        };
        Client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Memento/" + (typeof(AiHttpClient).Assembly.GetName().Version?.ToString(3) ?? "0"));
    }

    public HttpClient Client { get; }

    public void Dispose() => Client.Dispose();
}
