namespace Memento.Video.Sources;

/// <summary>A source paused or resumed at <see cref="AtQpc"/>; <see cref="Reason"/> is a short sentence for the footer ("Window minimised").</summary>
public sealed class VideoSourceStateChangedEventArgs(VideoSourceState state, string? reason, long atQpc) : EventArgs
{
    public VideoSourceState State { get; } = state;

    public string? Reason { get; } = reason;

    public long AtQpc { get; } = atQpc;
}
