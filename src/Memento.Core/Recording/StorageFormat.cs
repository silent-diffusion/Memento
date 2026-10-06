using Memento.Core.Settings;

namespace Memento.Core.Recording;

/// <summary>The storage codec and its options.</summary>
/// <param name="Codec"><c>flac</c>, <c>aac</c>, <c>mp3</c> or <c>wav</c>.</param>
public sealed record StorageFormat(string Codec, int? BitrateKbps, bool DownmixMono)
{
    public static StorageFormat From(StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new StorageFormat(settings.Codec, settings.IsLossy ? settings.BitrateKbps : null, settings.DownmixMono);
    }
}
