namespace Memento.Core.Audio;

/// <summary>Encoder options from Settings › Recording › storage.</summary>
/// <param name="BitrateKbps">For lossy codecs, 64–320; <c>null</c> for lossless.</param>
/// <param name="DownmixMono">Write one channel (average of all channels).</param>
public sealed record AudioEncodeOptions(int? BitrateKbps, bool DownmixMono);
