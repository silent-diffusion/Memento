using System.Runtime.InteropServices;
using LLama.Native;

namespace Memento.AI.Local;

/// <summary>
/// Lists ggml's backend devices through the exports of the loaded <c>ggml-base.dll</c>. With the Vulkan build this
/// is how much memory the graphics card has free for this process, the number the VRAM budget needs.
/// </summary>
internal static class GgmlDevices
{
    private delegate IntPtr StringFunction(IntPtr device);

    private delegate void MemoryFunction(IntPtr device, out UIntPtr free, out UIntPtr total);

    private delegate int TypeFunction(IntPtr device);

    public static IReadOnlyList<GgmlDevice> List()
    {
        var devices = new List<GgmlDevice>();
        var module = LlamaNative.ModuleHandle("ggml-base.dll");
        if (module == IntPtr.Zero
            || !NativeLibrary.TryGetExport(module, "ggml_backend_dev_name", out var namePtr)
            || !NativeLibrary.TryGetExport(module, "ggml_backend_dev_description", out var descriptionPtr)
            || !NativeLibrary.TryGetExport(module, "ggml_backend_dev_memory", out var memoryPtr)
            || !NativeLibrary.TryGetExport(module, "ggml_backend_dev_type", out var typePtr))
        {
            return devices;
        }

        var name = Marshal.GetDelegateForFunctionPointer<StringFunction>(namePtr);
        var description = Marshal.GetDelegateForFunctionPointer<StringFunction>(descriptionPtr);
        var memory = Marshal.GetDelegateForFunctionPointer<MemoryFunction>(memoryPtr);
        var type = Marshal.GetDelegateForFunctionPointer<TypeFunction>(typePtr);
        var count = (int)NativeApi.ggml_backend_dev_count();
        for (var i = 0; i < count; i++)
        {
            var device = NativeApi.ggml_backend_dev_get((UIntPtr)i);
            memory(device, out var free, out var total);
            var kind = type(device) switch
            {
                0 => "CPU",
                1 => "GPU",
                2 => "IGPU",
                3 => "ACCEL",
                var other => other.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            devices.Add(new GgmlDevice(i, Marshal.PtrToStringUTF8(name(device)) ?? "?", Marshal.PtrToStringUTF8(description(device)) ?? "?", kind, (long)free, (long)total));
        }

        return devices;
    }

    /// <summary>The discrete graphics card llama.cpp offloads to (main GPU 0), if any.</summary>
    public static GgmlDevice? DiscreteGpu() => List().FirstOrDefault(d => d.Type == "GPU");
}
