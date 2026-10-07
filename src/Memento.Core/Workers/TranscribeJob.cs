namespace Memento.Core.Workers;

/// <summary>
/// A transcription job: each track separately, in windows of <see cref="WindowSeconds"/> that overlap by
/// <see cref="OverlapSeconds"/>, Whisper.net with runtime order <see cref="Runtimes"/> (Vulkan, then CPU; never CUDA),
/// token timestamps and probabilities on, DTW off.
/// </summary>
/// <param name="Runtimes"><c>["vulkan","cpu"]</c> or <c>["cpu"]</c>.</param>
/// <param name="GpuDevice">Vulkan device index, or -1 to pick the device whose name matches <see cref="GpuName"/>.</param>
/// <param name="GpuName">The discrete graphics card the host chose (from DXGI).</param>
/// <param name="Language"><c>auto</c> or a language code.</param>
/// <param name="Prompt">A short punctuated prompt (large-v3-turbo is unpunctuated without one).</param>
public sealed record TranscribeJob(
    IReadOnlyList<WorkerTrack> Tracks,
    string ModelPath,
    string ModelId,
    IReadOnlyList<string> Runtimes,
    int GpuDevice,
    string? GpuName,
    string Language,
    string Prompt,
    int Threads,
    bool WordTimestamps,
    double WindowSeconds,
    double OverlapSeconds);
