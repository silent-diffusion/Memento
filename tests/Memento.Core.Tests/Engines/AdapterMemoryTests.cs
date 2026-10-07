using Memento.Core.Engines;
using Memento.Core.Engines.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Engines;

/// <summary>Free video memory counts what every process uses on the card, not only this process's DXGI budget.</summary>
public sealed class AdapterMemoryTests
{
    [Fact]
    public void AnAdapterThatDoesNotExistHasNoReading() =>
        Assert.Null(PdhAdapterMemory.DedicatedUsage(0xDEADBEEF, 0x7EADBEEF));

    [Fact]
    public void FreeVideoMemoryIsNeverMoreThanTheCardMinusWhatIsInUse()
    {
        var snapshot = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance).Sample();

        // On a PC without a graphics card there is nothing to check.
        foreach (var gpu in snapshot.Gpus.Where(g => g.FreeVramBytes is not null))
        {
            Assert.InRange(gpu.FreeVramBytes!.Value, 0, gpu.DedicatedVideoMemoryBytes);
        }
    }
}
