using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Video;

namespace Memento.Core.Settings;

/// <summary>Settings › Recording › Video defaults (2.0, DESIGN.md §18.5). Missing in 1.x files, which then read with the defaults.</summary>
public sealed record VideoSettings
{
    public const string SourceScreen = "screen";
    public const string SourceWindow = "window";
    public const string SourceCamera = "camera";
    public const string SourceScreenAndCamera = "screenAndCamera";
    public const string ResolutionDisplay = "display";
    public const string Resolution1080 = "1080p";
    public const string Resolution720 = "720p";
    public const string QualitySmaller = "smaller";
    public const string QualityBalanced = "balanced";
    public const string QualityBest = "best";

    public static IReadOnlyList<string> Sources { get; } = [SourceScreen, SourceWindow, SourceCamera, SourceScreenAndCamera];

    public static IReadOnlyList<string> Resolutions { get; } = [ResolutionDisplay, Resolution1080, Resolution720];

    public static IReadOnlyList<int> FrameRates { get; } = [24, 30, 60];

    public static IReadOnlyList<string> Qualities { get; } = [QualitySmaller, QualityBalanced, QualityBest];

    /// <summary>Recording mode Audio + video; the product default is Audio only.</summary>
    public bool RecordVideo { get; init; }

    public string DefaultSource { get; init; } = SourceScreen;

    public string Resolution { get; init; } = ResolutionDisplay;

    public int FrameRate { get; init; } = 30;

    public string Quality { get; init; } = QualityBalanced;

    public string? DefaultDisplayId { get; init; }

    public string? DefaultCameraId { get; init; }

    public string PipCorner { get; init; } = VideoPipChange.BottomRight;

    public bool HardwareEncoder { get; init; } = true;

    public bool IncludePointer { get; init; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (!Sources.Contains(DefaultSource, StringComparer.Ordinal))
        {
            return $"Default video source '{DefaultSource}' is not available. Choose {string.Join(", ", Sources)}.";
        }

        if (!Resolutions.Contains(Resolution, StringComparer.Ordinal))
        {
            return $"Video resolution '{Resolution}' is not available. Choose {string.Join(", ", Resolutions)}.";
        }

        if (!FrameRates.Contains(FrameRate))
        {
            return $"A frame rate of {FrameRate} fps is not available. Choose 24, 30 or 60.";
        }

        if (!Qualities.Contains(Quality, StringComparer.Ordinal))
        {
            return $"Video quality '{Quality}' is not available. Choose {string.Join(", ", Qualities)}.";
        }

        if (!VideoPipChange.IsCorner(PipCorner))
        {
            return $"Inset corner '{PipCorner}' is not available. Choose {string.Join(", ", VideoPipChange.Corners)}.";
        }

        if (DefaultDisplayId is { Length: > 512 } || DefaultCameraId is { Length: > 512 })
        {
            return "The remembered display or camera is not valid; choose it again.";
        }

        return null;
    }
}
