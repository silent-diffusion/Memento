namespace Memento.Core.Bridge.Contracts;

/// <summary><c>recording.video</c> for <c>settings.set</c>: part of the <c>recording</c> block, replaced whole; a field left out or <c>null</c> takes its default.</summary>
public sealed record VideoSettingsPatch
{
    public string? DefaultSource { get; init; }

    public string? Resolution { get; init; }

    public int? FrameRate { get; init; }

    public string? Quality { get; init; }

    public string? DefaultCameraId { get; init; }

    public string? PipCorner { get; init; }

    public bool? HardwareEncoder { get; init; }

    public bool? IncludePointer { get; init; }
}
