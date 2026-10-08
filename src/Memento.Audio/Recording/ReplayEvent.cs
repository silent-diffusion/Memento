namespace Memento.Audio.Recording;

/// <summary>
/// The subscriber list of an event that must never be missed (a lost source, the session stopping). A session
/// starts capturing inside <see cref="AudioRecordingSession.StartAsync"/>, before its caller can subscribe; on a
/// starved machine a source can be lost, and the event raised, in that window. Each handler therefore receives
/// every occurrence exactly once: live if it was subscribed when the occurrence was recorded, otherwise as a replay
/// when it subscribes.
/// </summary>
internal sealed class ReplayEvent<T>
    where T : EventArgs
{
    private readonly object _gate = new();
    private readonly List<T> _raised = [];
    private EventHandler<T>? _handlers;

    /// <summary>Subscribes <paramref name="handler"/>; returns the occurrences it missed, for the caller to deliver.</summary>
    public IReadOnlyList<T> Add(EventHandler<T>? handler)
    {
        if (handler is null)
        {
            return [];
        }

        lock (_gate)
        {
            _handlers += handler;
            return [.. _raised];
        }
    }

    public void Remove(EventHandler<T>? handler)
    {
        lock (_gate)
        {
            _handlers -= handler;
        }
    }

    /// <summary>Records an occurrence; returns the handlers to deliver it to now (later subscribers get a replay).</summary>
    public EventHandler<T>? Record(T args)
    {
        lock (_gate)
        {
            _raised.Add(args);
            return _handlers;
        }
    }
}
