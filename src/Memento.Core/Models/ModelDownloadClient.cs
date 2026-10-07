using System.Net;

namespace Memento.Core.Models;

/// <summary>The HTTP client the model manager downloads with (one per app; tests point it at a local server).</summary>
public sealed class ModelDownloadClient : IDisposable
{
    public ModelDownloadClient()
        : this(new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None, AllowAutoRedirect = true, MaxAutomaticRedirections = 10 }))
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
}
