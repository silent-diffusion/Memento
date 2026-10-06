namespace Memento.Audio.Writing;

/// <summary>A run of frames inside a packet.</summary>
internal readonly record struct FrameRange(int Offset, int Count);
