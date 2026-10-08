using System.Net;

namespace Memento.Core.Models;

/// <summary>
/// The HTTP client the model manager downloads with (one per app; tests point it at a local server). Together with
/// <c>AiHttpClient</c> it is the only place Memento creates an HTTP client. Redirects are followed by the handler; the
/// model manager checks the host that finally answered (<see cref="ModelDownloadHosts"/>).
/// </summary>
public sealed class ModelDownloadClient : IDisposable
{
    public ModelDownloadClient()
        : this(CreateHandler())
    {
    }

    /// <summary>A client over <paramref name="handler"/> (a test seam: a recording or refusing handler).</summary>
    public ModelDownloadClient(HttpMessageHandler handler)
        : this(new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler)), disposeHandler: true))
    {
    }

    public ModelDownloadClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        Http = http;
        Http.Timeout = Timeout.InfiniteTimeSpan;
        if (Http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Memento-model-manager/1");
        }
    }

    public HttpClient Http { get; }

    public void Dispose() => Http.Dispose();

    private static SocketsHttpHandler CreateHandler() =>
        new() { AutomaticDecompression = DecompressionMethods.None, AllowAutoRedirect = true, MaxAutomaticRedirections = 10 };
}
