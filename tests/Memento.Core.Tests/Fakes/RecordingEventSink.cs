using System.Text.Json;
using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

/// <summary>Collects posted events; thread-safe, because recording events arrive from several threads.</summary>
internal sealed class RecordingEventSink : IBridgeEventSink
{
    private readonly object _gate = new();
    private readonly List<string> _posted = [];

    public List<string> Posted
    {
        get
        {
            lock (_gate)
            {
                return [.. _posted];
            }
        }
    }

    public void Post(string eventJson)
    {
        lock (_gate)
        {
            _posted.Add(eventJson);
            Monitor.PulseAll(_gate);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _posted.Clear();
        }
    }

    /// <summary>Payloads of every event named <paramref name="name"/>, in order.</summary>
    public List<JsonElement> Payloads(string name)
    {
        var result = new List<JsonElement>();
        foreach (var json in Posted)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetProperty("event").GetString() == name)
            {
                result.Add(document.RootElement.GetProperty("payload").Clone());
            }
        }

        return result;
    }

    /// <summary>Waits until an event named <paramref name="name"/> whose payload matches arrives.</summary>
    public async Task<JsonElement> WaitForAsync(string name, Func<JsonElement, bool>? match = null, int timeoutMs = Patience.CeilingMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var found = Payloads(name).FirstOrDefault(p => match is null || match(p));
            if (found.ValueKind != JsonValueKind.Undefined)
            {
                return found;
            }

            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"No '{name}' event arrived within {timeoutMs} ms. Got: {string.Join(", ", Posted.Select(EventName).Distinct())}");
            }

            await Task.Delay(10);
        }
    }

    private static string EventName(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("event").GetString() ?? "?";
    }
}
