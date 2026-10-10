using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Recording › Video defaults for <c>settings.set</c>: each field present is changed, the others keep their value.</summary>
public sealed record VideoSettingsPatch
{
    public bool? RecordVideo { get; init; }

    public string? DefaultSource { get; init; }

    public string? Resolution { get; init; }

    public int? FrameRate { get; init; }

    public string? Quality { get; init; }

    /// <summary>A display id, or JSON <c>null</c> for the primary display; left out, it keeps its value.</summary>
    public JsonElement DefaultDisplayId { get; init; }

    /// <summary>A camera id, or JSON <c>null</c> for the first camera; left out, it keeps its value.</summary>
    public JsonElement DefaultCameraId { get; init; }

    public string? PipCorner { get; init; }

    public bool? HardwareEncoder { get; init; }

    public bool? IncludePointer { get; init; }
}
