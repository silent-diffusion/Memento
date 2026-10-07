namespace Memento.AI.Local;

/// <summary>How a model will be loaded (<see cref="LocalVramPlanner"/>).</summary>
/// <param name="UseGpu">Offload to the graphics card.</param>
/// <param name="Fits">The requested device can run it (false only when the graphics card was required and is too small).</param>
/// <param name="GpuLayers">Layers to offload (0 on the processor).</param>
/// <param name="NeededVramBytes">The estimate for the smallest useful GPU load, including the margin.</param>
/// <param name="Reason">A short factual note for logs and Settings.</param>
public sealed record LocalLlmPlan(bool UseGpu, bool Fits, int GpuLayers, int ContextTokens, long NeededVramBytes, string Reason);
