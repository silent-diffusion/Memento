using Memento.Core.Settings;

namespace Memento.Core.Recording;

/// <summary>The storage codec and its options.</summary>
/// <param name="Codec"><c>flac</c>, <c>aac</c>, <c>mp3</c> or <c>wav</c>.</param>
public sealed record StorageFormat(string Codec, int? BitrateKbps, bool DownmixMono)
{
    /// <summary>
    /// What finalize always writes: lossless FLAC with every channel. A smaller format chosen in Settings is applied
    /// later by the <c>optimize</c> stage (ARCHITECTURE.md §5, "Storage format options").
    /// </summary>
    public static StorageFormat Lossless { get; } = new(StorageSettings.Flac, null, false);

    public static StorageFormat From(StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new StorageFormat(settings.Codec, settings.IsLossy ? settings.BitrateKbps : null, settings.DownmixMono);
    }
}
