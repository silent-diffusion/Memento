using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

internal sealed class RecordingEventSink : IBridgeEventSink
{
    public List<string> Posted { get; } = [];

    public void Post(string eventJson) => Posted.Add(eventJson);
}
