namespace Memento.Core.Audio;

/// <summary>
/// Encodes a finished WAV file to a storage format. Core ships <see cref="PassThroughWavEncoder"/>;
/// Memento.Audio adds Media Foundation encoders for <c>flac</c>, <c>aac</c> and <c>mp3</c> by registering
/// more <see cref="IAudioEncoder"/> singletons. The finalizer picks the one whose <see cref="Codec"/> matches Settings.
/// </summary>
public interface IAudioEncoder
{
    /// <summary><c>wav</c>, <c>flac</c>, <c>aac</c> or <c>mp3</c> (Settings › Recording › storage codec).</summary>
    string Codec { get; }

    /// <summary>File extension including the dot, e.g. <c>.flac</c>; <c>.m4a</c> for AAC.</summary>
    string FileExtension { get; }

    /// <summary>Lossy codecs honour <see cref="AudioEncodeOptions.BitrateKbps"/>.</summary>
    bool IsLossless { get; }

    /// <summary>
    /// Reads <paramref name="sourceWavPath"/> and writes <paramref name="destinationPath"/>. Must not modify the source.
    /// The destination may exist from an interrupted earlier attempt and is overwritten.
    /// </summary>
    /// <exception cref="IOException">Writing failed; <see cref="DiskErrors.IsDiskFull"/> tells a full drive apart.</exception>
    Task EncodeAsync(string sourceWavPath, string destinationPath, AudioEncodeOptions options, CancellationToken cancellationToken);
}
