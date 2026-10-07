namespace Memento.AI.Local;

/// <summary>
/// Free memory of the discrete graphics card as llama.cpp's Vulkan backend reports it (the budget Windows gives this
/// process). With the CPU build loaded there is no device and the reading is <c>null</c>.
/// </summary>
public sealed class GgmlVramProbe : IGpuVramProbe
{
    public GpuVramReading? Read()
    {
        if (LlamaNative.Backend != "vulkan")
        {
            return null;
        }

        var gpu = GgmlDevices.DiscreteGpu();
        return gpu is null ? null : new GpuVramReading(gpu.Description, gpu.FreeBytes, gpu.TotalBytes);
    }
}
