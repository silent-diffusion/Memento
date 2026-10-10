namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Transcription for <c>settings.set</c>. Each omitted or <c>null</c> field keeps its value.</summary>
public sealed record TranscriptionSettingsPatch
{
    public bool? Auto { get; init; }

    public string? Timing { get; init; }

    public bool? PauseWhenBusy { get; init; }

    public string? ModelId { get; init; }

    public string? CpuFallbackModelId { get; init; }

    public string? Language { get; init; }

    public bool? KeepWordTimestamps { get; init; }

    public double? LowConfidenceThreshold { get; init; }

    /// <summary>2.0: the live transcript may use the graphics card.</summary>
    public bool? LiveOnGpu { get; init; }
}
