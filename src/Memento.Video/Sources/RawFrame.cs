namespace Memento.Video.Sources;

/// <summary>One picture waiting in a <see cref="RawFrameQueue"/>: the first <see cref="Length"/> bytes of <see cref="Buffer"/>, stamped on the QPC clock.</summary>
internal sealed record RawFrame(byte[] Buffer, int Length, long Qpc);
