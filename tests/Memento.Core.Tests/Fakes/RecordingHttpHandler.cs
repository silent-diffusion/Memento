using System.Net;

namespace Memento.Core.Tests.Fakes;

/// <summary>An <see cref="HttpMessageHandler"/> that answers every request itself and records the addresses asked for; nothing reaches the network.</summary>
internal sealed class RecordingHttpHandler(HttpStatusCode status = HttpStatusCode.NotFound) : HttpMessageHandler
{
    private readonly List<Uri> _requests = [];

    public IReadOnlyList<Uri> Requests
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
            _requests.Add(request.RequestUri!);
        }

        return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent([]) });
    }
}
