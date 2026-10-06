namespace Memento.Audio.Codecs;

/// <summary>Why an encode failed; each maps to a specific message and remedy.</summary>
public enum AudioEncodeErrorCode
{
    /// <summary>%TEMP% has less free space than the MF sink's temporary file needs (1.1 × the WAV size).</summary>
    InsufficientTempSpace,

    /// <summary>The input format cannot be encoded (FLAC takes 16/24-bit integer PCM only).</summary>
    UnsupportedInput,

    /// <summary>The Media Foundation encoder is not installed or offers no matching setting.</summary>
    EncoderUnavailable,

    /// <summary>The output file already exists; finalized files are never overwritten.</summary>
    OutputExists,

    /// <summary>Media Foundation failed, or the result did not verify.</summary>
    EncodeFailed,
}
