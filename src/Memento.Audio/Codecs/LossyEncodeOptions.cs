namespace Memento.Audio.Codecs;

/// <summary>What <see cref="MediaFoundationLossyEncoder"/> produces.</summary>
public sealed record LossyEncodeOptions(LossyCodec Codec, int BitrateKbps)
{
    /// <summary>Average all channels into one before encoding (halves the size of speech recordings).</summary>
    public bool DownmixToMono { get; init; }

    /// <summary>Replace an existing output file.</summary>
    public bool Overwrite { get; init; }

    public string FileExtension => Codec == LossyCodec.Mp3 ? ".mp3" : ".m4a";
}
