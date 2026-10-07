using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Memento.Core.Settings;

/// <summary>Settings › Transcription (M2). Missing in M1 files, which then read with the defaults.</summary>
public sealed partial record TranscriptionSettings
{
    public const string TimingAfter = "after";
    public const string TimingDuring = "during";
    public const string AutoLanguage = "auto";
    public const string DefaultCpuFallbackModelId = "whisper-small";
    public const double DefaultLowConfidenceThreshold = 0.5;
    public const double MinLowConfidenceThreshold = 0.05;
    public const double MaxLowConfidenceThreshold = 0.95;

    public static IReadOnlyList<string> Timings { get; } = [TimingAfter, TimingDuring];

    /// <summary>Transcribe automatically after recording.</summary>
    public bool Auto { get; init; } = true;

    /// <summary><see cref="TimingAfter"/> or <see cref="TimingDuring"/> (the live draft; the full pass still runs after).</summary>
    public string Timing { get; init; } = TimingAfter;

    /// <summary>Pause transcription while recording and while the PC is busy.</summary>
    public bool PauseWhenBusy { get; init; } = true;

    /// <summary>The chosen model; <c>null</c> means the recommended one for this PC, decided when it is needed.</summary>
    public string? ModelId { get; init; }

    /// <summary>The smaller model offered when the chosen one fails or is too slow.</summary>
    public string CpuFallbackModelId { get; init; } = DefaultCpuFallbackModelId;

    /// <summary><see cref="AutoLanguage"/> or a BCP-47 primary subtag such as <c>en</c>.</summary>
    public string Language { get; init; } = AutoLanguage;

    public bool KeepWordTimestamps { get; init; } = true;

    public double LowConfidenceThreshold { get; init; } = DefaultLowConfidenceThreshold;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static bool IsValidLanguage(string? language) =>
        language is not null && (language == AutoLanguage || LanguagePattern().IsMatch(language));

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (!Timings.Contains(Timing, StringComparer.Ordinal))
        {
            return $"Transcription timing '{Timing}' is not available. Choose {string.Join(" or ", Timings)}.";
        }

        if (!IsValidLanguage(Language))
        {
            return $"Language '{Language}' is not available. Choose auto or a two- or three-letter language code such as en.";
        }

        if (double.IsNaN(LowConfidenceThreshold) || LowConfidenceThreshold is < MinLowConfidenceThreshold or > MaxLowConfidenceThreshold)
        {
            return $"A low-confidence threshold of {LowConfidenceThreshold} is out of range. Choose {MinLowConfidenceThreshold} to {MaxLowConfidenceThreshold}.";
        }

        if (ModelId is { } model && (model.Length is 0 or > 64))
        {
            return "The transcription model id is not valid; choose a model again.";
        }

        if (CpuFallbackModelId.Length is 0 or > 64)
        {
            return "The fallback model id is not valid; choose a model again.";
        }

        return null;
    }

    [GeneratedRegex("^[a-z]{2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();
}
