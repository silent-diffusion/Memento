namespace Memento.Video.Sources;

/// <summary>Receives one picture from an <see cref="IRawFrameFeed"/>: <paramref name="length"/> bytes at <paramref name="data"/>, valid only during the call.</summary>
internal delegate void RawFrameHandler(IntPtr data, int length);
