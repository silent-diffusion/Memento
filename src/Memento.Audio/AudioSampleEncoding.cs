namespace Memento.Audio;

/// <summary>How samples are stored: signed little-endian integers or IEEE floats.</summary>
public enum AudioSampleEncoding
{
    /// <summary>Signed integer PCM (unsigned for 8-bit).</summary>
    Pcm,

    /// <summary>IEEE 754 float, nominally in [-1, 1].</summary>
    IeeeFloat,
}
