using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Settings;

/// <summary>Storage format for finalized tracks and the mix (Settings › Recording).</summary>
public sealed record StorageSettings
{
    public const string Flac = "flac";
    public const string Aac = "aac";
    public const string Mp3 = "mp3";
    public const int MinBitrateKbps = 64;
    public const int MaxBitrateKbps = 320;
    public const int DefaultLossyBitrateKbps = 192;

    public static IReadOnlyList<string> Codecs { get; } = [Flac, Aac, Mp3];

    public string Codec { get; init; } = Flac;

    /// <summary>Lossy codecs only; <c>null</c> for FLAC.</summary>
    public int? BitrateKbps { get; init; }

    public bool DownmixMono { get; init; }

    /// <summary>Recorded for later; separate tracks are always kept in M1.</summary>
    public bool KeepOnlyMix { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool IsLossy => Codec != Flac;

    public static bool IsValidCodec(string? codec) => codec is not null && Codecs.Contains(codec, StringComparer.Ordinal);

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (!IsValidCodec(Codec))
        {
            return $"Storage format '{Codec}' is not available. Choose {string.Join(", ", Codecs)}.";
        }

        if (IsLossy && BitrateKbps is not (>= MinBitrateKbps and <= MaxBitrateKbps))
        {
            return $"{Codec.ToUpperInvariant()} needs a bitrate of {MinBitrateKbps} to {MaxBitrateKbps} kbps.";
        }

        return null;
    }
}
