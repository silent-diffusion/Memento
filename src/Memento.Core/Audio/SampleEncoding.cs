namespace Memento.Core.Audio;

/// <summary>How samples are stored in a PCM stream.</summary>
public enum SampleEncoding
{
    /// <summary>Signed integers (8-bit unsigned per the WAV convention), WAVE_FORMAT_PCM.</summary>
    Pcm,

    /// <summary>32-bit IEEE floats, WAVE_FORMAT_IEEE_FLOAT. The usual WASAPI shared-mode mix format.</summary>
    IeeeFloat,
}
