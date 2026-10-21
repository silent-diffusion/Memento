namespace Memento.AI.Local;

/// <summary>
/// What a load of a local model actually took, as the worker reported it (<see cref="LocalLlmDeviceInfo"/> and the
/// answers' speed): kept per model so the next <see cref="MemoryPlanner"/> plan is checked and corrected against it.
/// </summary>
/// <param name="GpuLayers">llama.cpp's <c>n_gpu_layers</c> as loaded.</param>
/// <param name="CpuExpertLayers">Blocks whose experts were in RAM.</param>
/// <param name="VramBytes">This worker's dedicated video memory after loading.</param>
/// <param name="RamBytes">This worker's RAM while writing (its working set).</param>
/// <param name="TokensPerSecond">Output tokens per second over the answers of the load (0 when none was written).</param>
/// <param name="PlannedVramBytes">The plan's video memory for this load.</param>
/// <param name="PlannedRamBytes">The plan's RAM for this load.</param>
public sealed record LocalLoadMeasurement(
    string ModelId,
    int GpuLayers,
    int CpuExpertLayers,
    int ContextTokens,
    long VramBytes,
    long RamBytes,
    double TokensPerSecond,
    long PlannedVramBytes,
    long PlannedRamBytes,
    DateTimeOffset At);
