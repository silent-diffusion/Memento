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
    public bool Auto { get; set; } = true;

    /// <summary>
    /// <see cref="TimingAfter"/> or <see cref="TimingDuring"/> (2.0: Settings › Transcription › Live transcript while
    /// recording; the provisional draft from the Small or Base model, never saved, while the full pass still runs after
    /// and replaces it). Off by default.
    /// </summary>
    public string Timing { get; set; } = TimingAfter;

    /// <summary>2.0: the live transcript may run on the graphics card; it runs on the processor otherwise. Off by default.</summary>
    public bool LiveOnGpu { get; init; }

    [JsonIgnore]
    public bool LiveDuringRecording => Timing == TimingDuring;

    /// <summary>Pause transcription while recording and while the PC is busy.</summary>
    public bool PauseWhenBusy { get; set; } = true;

    /// <summary>The chosen model; <c>null</c> means the recommended one for this PC, decided when it is needed.</summary>
    public string? ModelId { get; init; }

    /// <summary>The smaller model offered when the chosen one fails or is too slow.</summary>
    public string CpuFallbackModelId { get; set; } = DefaultCpuFallbackModelId;

    /// <summary><see cref="AutoLanguage"/> or a BCP-47 primary subtag such as <c>en</c>.</summary>
    public string Language { get; set; } = AutoLanguage;

    public bool KeepWordTimestamps { get; set; } = true;

    public double LowConfidenceThreshold { get; set; } = DefaultLowConfidenceThreshold;

    /// <summary>
    /// 2.6.0: Settings › Transcription › Keep speech heard twice once. A microphone line that says what a system-audio line
    /// says at the same time (the microphone heard the speakers) is left out of a fresh transcript (<c>BleedFilter</c>).
    /// On unless switched off.
    /// </summary>
    public bool DropMicrophoneBleed { get; set; } = true;

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
