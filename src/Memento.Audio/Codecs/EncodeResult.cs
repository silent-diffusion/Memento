namespace Memento.Audio.Codecs;

/// <summary>A finished encode.</summary>
/// <param name="OutputPath">The file written (moved into place atomically).</param>
/// <param name="Bytes">Its size.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the file, for the manifest's <c>integrity</c>.</param>
/// <param name="Duration">Duration of the encoded audio (input frames / rate).</param>
/// <param name="BitrateKbps">Lossy only: the bitrate the encoder actually used.</param>
public sealed record EncodeResult(string OutputPath, long Bytes, string Sha256, TimeSpan Duration, int? BitrateKbps = null);
