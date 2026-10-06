namespace Memento.Audio.Recording;

public sealed class CheckpointEventArgs(SessionCheckpoint checkpoint) : EventArgs
{
    public SessionCheckpoint Checkpoint { get; } = checkpoint;
}
