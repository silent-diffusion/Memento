using Memento.Audio.Capture;

namespace Memento.Audio.Recording;

/// <summary>A source went away; its track ended at <see cref="At"/> and the others continue (BRIDGE.md <c>recording.sourceLost</c>).</summary>
public sealed class SourceLostEventArgs(string sourceId, string name, TimeSpan at, CaptureLostReason reason, string message, IReadOnlyList<string> remaining) : EventArgs
{
    public string SourceId { get; } = sourceId;

    public string Name { get; } = name;

    /// <summary>Timeline time the track ended.</summary>
    public TimeSpan At { get; } = at;

    public CaptureLostReason Reason { get; } = reason;

    /// <summary>Specific, user-facing message (what, when, what is safe, what to do).</summary>
    public string Message { get; } = message;

    /// <summary>Source ids still recording.</summary>
    public IReadOnlyList<string> Remaining { get; } = remaining;
}
