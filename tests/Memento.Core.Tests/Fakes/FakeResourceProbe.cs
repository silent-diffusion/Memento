using Memento.Core.Engines;

namespace Memento.Core.Tests.Fakes;

/// <summary>A PC under test control: set <see cref="Snapshot"/>.</summary>
internal sealed class FakeResourceProbe : IResourceProbe
{
    public static readonly GpuInfo Rtx = new(0, "NVIDIA GeForce RTX 3060 Laptop GPU", 0x10DE, 6L << 30, 5L << 30, IsDiscrete: true);

    /// <summary>Ollama's model server holding 5 GB, as on the product owner's PC.</summary>
    public static readonly GpuMemoryHolder Ollama = new("llama-server.exe", "Ollama (llama-server.exe)", 5L << 30);

    public ResourceSnapshot Snapshot { get; set; } = ResourceSnapshot.Empty;

    /// <summary>How many times <see cref="Refresh"/> was asked for (Settings' "Check again").</summary>
    public int Refreshes { get; private set; }

    public ResourceSnapshot Sample() => Snapshot;

    public ResourceSnapshot Refresh()
    {
        Refreshes++;
        return Snapshot;
    }

    public static ResourceSnapshot WithGpu(long freeVramBytes, params GpuMemoryHolder[] holders) =>
        ResourceSnapshot.Empty with { Gpus = [Rtx with { FreeVramBytes = freeVramBytes, Holders = holders, UsedBytes = (6L << 30) - freeVramBytes }] };
}
