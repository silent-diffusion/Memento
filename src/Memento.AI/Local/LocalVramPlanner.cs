using System.Globalization;

namespace Memento.AI.Local;

/// <summary>
/// The video memory budget (ENGINE-NOTES.md section H, trap 2): from the free VRAM measured just before loading,
/// pick the largest context (default, then halved down to the profile minimum) whose full GPU load plus a margin
/// fits; otherwise run on the processor (or, when the graphics card was required, report that it does not fit).
/// Partial offload is not chosen automatically: on the reference laptop it is slower than either extreme once the
/// card is short of memory, and Windows silently spills instead of failing.
/// </summary>
public static class LocalVramPlanner
{
    public static LocalLlmPlan Plan(LocalModelProfile profile, string device, long? freeVramBytes, int requestedContext, long marginBytes, int? forcedGpuLayers = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var allLayers = profile.Layers + 1;
        var gpuContext = requestedContext > 0 ? requestedContext : profile.ContextTokens;
        var cpuContext = requestedContext > 0 ? requestedContext : profile.CpuContextTokens;
        var minimum = profile.VramBytes(Math.Min(profile.MinContextTokens, gpuContext)) + marginBytes;

        if (device == LocalLlmDevices.Cpu)
        {
            return new LocalLlmPlan(false, true, 0, cpuContext, minimum, "processor requested");
        }

        if (forcedGpuLayers is { } forced)
        {
            return new LocalLlmPlan(forced > 0, true, Math.Min(forced, allLayers), gpuContext, minimum, string.Create(CultureInfo.InvariantCulture, $"{forced} layers requested"));
        }

        if (freeVramBytes is not { } free || free <= 0)
        {
            return device == LocalLlmDevices.Gpu
                ? new LocalLlmPlan(false, false, 0, gpuContext, minimum, "no graphics card memory reading")
                : new LocalLlmPlan(false, true, 0, cpuContext, minimum, "no graphics card memory reading");
        }

        for (var context = gpuContext; context >= Math.Min(profile.MinContextTokens, gpuContext); context /= 2)
        {
            var needed = profile.VramBytes(context) + marginBytes;
            if (free >= needed)
            {
                return new LocalLlmPlan(true, true, allLayers, context, needed, string.Create(CultureInfo.InvariantCulture, $"fits: {Mb(needed)} of {Mb(free)} free"));
            }

            if (context / 2 < Math.Min(profile.MinContextTokens, gpuContext))
            {
                break;
            }
        }

        var reason = string.Create(CultureInfo.InvariantCulture, $"needs {Mb(minimum)}, {Mb(free)} free");
        return device == LocalLlmDevices.Gpu
            ? new LocalLlmPlan(false, false, 0, gpuContext, minimum, reason)
            : new LocalLlmPlan(false, true, 0, cpuContext, minimum, reason + "; using the processor");
    }

    private static string Mb(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024 * 1024)} MB");
}
