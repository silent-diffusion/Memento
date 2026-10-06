namespace Memento.Core.Host;

/// <summary>Delivers a serialized bridge event to the UI. Implementations drop the event if no page is loaded.</summary>
public interface IBridgeEventSink
{
    void Post(string eventJson);
}
