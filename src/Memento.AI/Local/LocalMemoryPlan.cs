namespace Memento.AI.Local;

/// <summary>
/// How <see cref="MemoryPlanner"/> places a local model before it is loaded: on the graphics card, split between the card
/// and RAM, with a mixture of experts' experts in RAM and the rest on the card, or in RAM on the processor; or that it
/// does not fit (with the amounts for the §17 sentence).
/// </summary>
/// <param name="Fits">The model fits in RAM and video memory with every reserve kept.</param>
/// <param name="Placement">One of <see cref="LocalPlacements"/>.</param>
/// <param name="GpuLayers">llama.cpp's <c>n_gpu_layers</c>: blocks on the card plus the output layer (0 in RAM only).</param>
/// <param name="TotalLayers">The model's transformer blocks (the output layer is not counted).</param>
/// <param name="CardBlocks">Transformer blocks on the card.</param>
/// <param name="CpuExpertLayers">Mixture of experts: the blocks (from the first) whose routed experts stay in RAM.</param>
/// <param name="ContextTokens">The context to load with.</param>
/// <param name="Threads">Processor threads for the layers in RAM.</param>
/// <param name="VramBytes">Planned video memory, without the margin.</param>
/// <param name="RamBytes">Planned RAM while writing (the weights read from RAM, the KV cache there, buffers, the worker).</param>
/// <param name="TokensPerSecond">The estimated writing speed (a measurement of the same placement replaces it).</param>
/// <param name="NeededBytes">RAM plus video memory the smallest placement needs, with the reserves (for the §17 sentence).</param>
/// <param name="FreeBytes">RAM plus usable video memory free now, after the reserves are counted as needed (for the §17 sentence).</param>
/// <param name="Reason">A short factual note for logs.</param>
public sealed record LocalMemoryPlan(
    bool Fits,
    string Placement,
    int GpuLayers,
    int TotalLayers,
    int CardBlocks,
    int CpuExpertLayers,
    int ContextTokens,
    int Threads,
    long VramBytes,
    long RamBytes,
    double TokensPerSecond,
    long NeededBytes,
    long FreeBytes,
    string Reason)
{
    /// <summary>The worker's device: the CPU build in RAM only, the Vulkan build (and the graphics card's gate) otherwise.</summary>
    public string Device => Placement == LocalPlacements.Ram ? LocalLlmDevices.Cpu : LocalLlmDevices.Gpu;

    /// <summary>The plan uses the graphics card.</summary>
    public bool UsesCard => Fits && Placement != LocalPlacements.Ram;
}
