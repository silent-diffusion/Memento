namespace Memento.Core.Workers;

/// <summary>Where the engine actually runs, reported once the model is loaded.</summary>
/// <param name="Runtime"><c>vulkan</c> or <c>cpu</c>.</param>
/// <param name="Device">"GPU (Vulkan)" or "CPU".</param>
public sealed record WorkerDevice(string Runtime, string Device, string? GpuName, int? GpuIndex, string EngineVersion);
