namespace Memento.Audio.Codecs;

/// <summary>Options for <see cref="MediaFoundationFlacEncoder"/>.</summary>
public sealed record FlacEncodeOptions
{
    public static FlacEncodeOptions Default { get; } = new();

    /// <summary>Decode the result and compare it byte for byte with the WAV before moving it into place.</summary>
    public bool VerifyBitExact { get; init; } = true;

    /// <summary>Replace an existing output file. Off by default: finalized tracks are never overwritten.</summary>
    public bool Overwrite { get; init; }
}
