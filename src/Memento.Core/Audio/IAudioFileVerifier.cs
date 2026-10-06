namespace Memento.Core.Audio;

/// <summary>
/// Decodes a whole audio file to prove it plays before anything it replaces is removed (the <c>optimize</c> stage).
/// Memento.Audio implements it with Media Foundation; without it, nothing is converted.
/// </summary>
public interface IAudioFileVerifier
{
    /// <summary>Reads every sample of <paramref name="path"/> and reports what it decoded to.</summary>
    /// <exception cref="InvalidDataException">The file does not decode.</exception>
    Task<AudioFileCheck> VerifyAsync(string path, CancellationToken cancellationToken);
}
