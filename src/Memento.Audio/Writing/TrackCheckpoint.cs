namespace Memento.Audio.Writing;

/// <summary>State of one track right after a checkpoint: everything described here is on disk.</summary>
public sealed record TrackCheckpoint(IReadOnlyList<string> Parts, long DataBytes, long Frames, TimeSpan Duration);
