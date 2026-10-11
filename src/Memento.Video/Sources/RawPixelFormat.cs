namespace Memento.Video.Sources;

/// <summary>
/// The uncompressed pictures a DirectShow camera can deliver that <see cref="RawFrameConverter"/> turns into NV12.
/// Compressed ones (MJPG and others) are decoded by DirectShow's own decoder in the graph first (ENGINE-NOTES.md §R).
/// </summary>
public enum RawPixelFormat
{
    /// <summary>Not one Memento reads.</summary>
    Unsupported,

    /// <summary>4:2:0, a luma plane then interleaved U/V (what OBS's virtual camera sends).</summary>
    Nv12,

    /// <summary>4:2:0, three planes: Y, U, V (also IYUV).</summary>
    I420,

    /// <summary>4:2:0, three planes: Y, V, U.</summary>
    Yv12,

    /// <summary>4:2:2 packed Y0 U Y1 V (most USB webcams' uncompressed format).</summary>
    Yuy2,

    /// <summary>4:2:2 packed U Y0 V Y1.</summary>
    Uyvy,

    /// <summary>B, G, R; rows bottom-up unless the height is negative.</summary>
    Rgb24,

    /// <summary>B, G, R, X (or A); rows bottom-up unless the height is negative.</summary>
    Rgb32,
}
