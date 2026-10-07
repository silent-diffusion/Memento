namespace Memento.Core.Engines;

/// <summary>What the PC has free right now (ARCHITECTURE.md §6, "Resource probe").</summary>
/// <param name="Gpus">Hardware adapters, discrete ones first; empty on CPU-only PCs or when DXGI is unavailable.</param>
/// <param name="CpuBusyPercent">Whole-system processor use since the previous sample, 0–100; <c>null</c> on the first sample.</param>
/// <param name="LogicalProcessors">Processor threads.</param>
public sealed record ResourceSnapshot(
    IReadOnlyList<GpuInfo> Gpus,
    double? CpuBusyPercent,
    int LogicalProcessors,
    long TotalRamBytes,
    long AvailableRamBytes)
{
    public static ResourceSnapshot Empty { get; } = new([], null, Environment.ProcessorCount, 0, 0);

    /// <summary>The graphics card transcription should use: the discrete one with the most free video memory.</summary>
    public GpuInfo? DiscreteGpu => Gpus.Where(g => g.IsDiscrete).OrderByDescending(g => g.FreeVramBytes ?? g.DedicatedVideoMemoryBytes).FirstOrDefault();
}
