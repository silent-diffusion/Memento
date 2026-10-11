namespace Memento.Video.Sources;

/// <summary>
/// The picture format a DirectShow graph delivers to Memento: what each buffer holds and how to read it.
/// </summary>
/// <param name="Pixel">The layout; <see cref="RawPixelFormat.Unsupported"/> when Memento can't read it.</param>
/// <param name="Width">Pixels per row of the picture.</param>
/// <param name="Height">Rows of the picture (positive).</param>
/// <param name="Stride">Bytes per row of the first plane in the buffer (DWORD-aligned for RGB).</param>
/// <param name="BottomUp">RGB rows stored last row first (a positive <c>biHeight</c>).</param>
/// <param name="FrameRate">Frames per second the format promises (0 when unknown).</param>
/// <param name="SubtypeName">The subtype as a short name ("NV12", "YUY2", "MJPG") for logs and messages.</param>
public sealed record RawVideoFormat(RawPixelFormat Pixel, int Width, int Height, int Stride, bool BottomUp, double FrameRate, string SubtypeName)
{
    /// <summary>The bytes a buffer needs to hold one picture of this format.</summary>
    public int MinimumLength => Pixel switch
    {
        RawPixelFormat.Nv12 => (Stride * Height) + (Stride * ChromaRows),
        RawPixelFormat.I420 or RawPixelFormat.Yv12 => (Stride * Height) + (2 * (Stride / 2) * ChromaRows),
        RawPixelFormat.Unsupported => 0,
        _ => Stride * Height,
    };

    /// <summary>Rows of a 4:2:0 chroma plane.</summary>
    public int ChromaRows => (Height + 1) / 2;

    /// <summary>The NV12 size the picture becomes: even width and height (an odd last row or column is cut).</summary>
    public (int Width, int Height) EvenSize => (Width & ~1, Height & ~1);
}
