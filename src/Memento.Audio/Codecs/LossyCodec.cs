namespace Memento.Audio.Codecs;

/// <summary>Lossy storage formats offered after processing (ARCHITECTURE.md §5).</summary>
public enum LossyCodec
{
    /// <summary>MPEG-1 Layer III, 96–320 kbit/s, <c>.mp3</c>.</summary>
    Mp3,

    /// <summary>AAC-LC in MPEG-4, 16–320 kbit/s requested (the encoder picks its nearest setting), <c>.m4a</c>.</summary>
    Aac,
}
