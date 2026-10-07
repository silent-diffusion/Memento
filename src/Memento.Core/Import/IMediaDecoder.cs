namespace Memento.Core.Import;

/// <summary>
/// Reads audio from any file Windows can decode (Memento.Audio implements it with Media Foundation; Core's
/// <see cref="WavMediaDecoder"/> reads WAV only). Video containers give their first audio stream.
/// </summary>
public interface IMediaDecoder
{
    /// <summary>Opens the file and reports its audio format and length.</summary>
    /// <exception cref="InvalidDataException">The file has no audio Windows can decode; the message says why.</exception>
    Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Decodes the whole file to 24-bit PCM WAV at its own sample rate and channel count, as
    /// <c>&lt;directory&gt;\&lt;stem&gt;.wav</c>, rolling over to <c>&lt;stem&gt;.part2.wav</c>, … at 3.5 GiB like a recording.
    /// </summary>
    /// <param name="progress">0..1 by decoded time.</param>
    /// <exception cref="InvalidDataException">The audio could not be decoded.</exception>
    Task<DecodedWav> DecodeToWavAsync(string path, string directory, string stem, IProgress<double>? progress, CancellationToken cancellationToken);
}
