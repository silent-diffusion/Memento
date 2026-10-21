namespace Memento.AI.Local;

/// <summary>Where the model was loaded (<c>device</c> line and result).</summary>
/// <param name="Backend"><c>vulkan</c> or <c>cpu</c>.</param>
/// <param name="GpuName">The graphics card, when offloaded.</param>
/// <param name="GpuLayers">Layers offloaded (0 on the processor): blocks on the card plus the output layer.</param>
/// <param name="FreeVramBytes">Free video memory when the plan was made.</param>
/// <param name="PlannedVramBytes">The budget's estimate for this load.</param>
/// <param name="CpuExpertLayers">Mixture of experts: the blocks whose routed experts were kept in RAM.</param>
/// <param name="RamBytes">This worker's RAM after loading and warming up (its working set: the weights read from RAM, the buffers).</param>
/// <param name="Reduced">Set when less went on the card than planned, with why ("the card had 1.2 GB less free", "spilled 0.6 GB").</param>
public sealed record LocalLlmDeviceInfo(
    string Backend,
    string? GpuName,
    int GpuLayers,
    int ContextTokens,
    int Threads,
    long? FreeVramBytes = null,
    long? PlannedVramBytes = null,
    int CpuExpertLayers = 0,
    long? RamBytes = null,
    string? Reduced = null);
