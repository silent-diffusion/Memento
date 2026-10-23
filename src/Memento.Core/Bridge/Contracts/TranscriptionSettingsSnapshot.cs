namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Transcription in <see cref="SettingsSnapshot"/>.</summary>
/// <param name="Timing"><c>after</c> or <c>during</c> (adds the live draft; the full pass still runs after).</param>
/// <param name="ModelId">The model in effect: the saved choice, or the recommended one for this PC.</param>
/// <param name="Language"><c>auto</c> or a BCP-47 primary subtag.</param>
/// <param name="LiveOnGpu">2.0: the live transcript (Timing <c>during</c>) may use the graphics card; it runs on the processor otherwise.</param>
/// <param name="DropMicrophoneBleed">2.6.0: a microphone line that repeats a system-audio line at the same time is left out of a fresh transcript.</param>
public sealed record TranscriptionSettingsSnapshot(
    bool Auto,
    string Timing,
    bool PauseWhenBusy,
    string ModelId,
    string CpuFallbackModelId,
    string Language,
    bool KeepWordTimestamps,
    double LowConfidenceThreshold,
    bool LiveOnGpu = false,
    bool DropMicrophoneBleed = true);
