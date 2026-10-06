namespace Memento.Core.Bridge.Contracts;

/// <summary>Storage format (ARCHITECTURE.md §5): lossless FLAC by default, or AAC/MP3 at a bitrate.</summary>
/// <param name="Codec"><c>flac</c>, <c>aac</c> or <c>mp3</c>.</param>
/// <param name="BitrateKbps">64–320 for lossy codecs; <c>null</c> for FLAC.</param>
public sealed record StorageSettingsSnapshot(string Codec, int? BitrateKbps, bool DownmixMono, bool KeepOnlyMix);
