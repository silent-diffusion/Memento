namespace Memento.AI.Local;

/// <summary>This process's GPU memory as Windows accounts it: on the card (dedicated) and in system RAM (shared).</summary>
public sealed record GpuProcessMemorySample(long DedicatedBytes, long SharedBytes);
