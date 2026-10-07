namespace Memento.AI.Local;

/// <summary>Where the model was loaded (<c>device</c> line and result).</summary>
/// <param name="Backend"><c>vulkan</c> or <c>cpu</c>.</param>
/// <param name="GpuName">The graphics card, when offloaded.</param>
/// <param name="GpuLayers">Layers offloaded (0 on the processor).</param>
/// <param name="FreeVramBytes">Free video memory when the plan was made.</param>
/// <param name="PlannedVramBytes">The budget's estimate for this load.</param>
public sealed record LocalLlmDeviceInfo(
    string Backend,
    string? GpuName,
    int GpuLayers,
    int ContextTokens,
    int Threads,
    long? FreeVramBytes = null,
    long? PlannedVramBytes = null);
