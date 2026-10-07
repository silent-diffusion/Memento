using System.Net;

namespace Memento.AI.Tests.Fakes;

/// <summary>An <see cref="HttpMessageHandler"/> that answers every request with one status and records it; nothing reaches the network.</summary>
internal sealed class RecordingHttpHandler(HttpStatusCode status) : HttpMessageHandler
{
    private readonly List<(Uri Uri, IReadOnlyList<string> HeaderNames)> _requests = [];

    public IReadOnlyList<(Uri Uri, IReadOnlyList<string> HeaderNames)> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add((request.RequestUri!, request.Headers.Select(h => h.Key).ToList()));
        }

        return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent("{}") });
    }
}
