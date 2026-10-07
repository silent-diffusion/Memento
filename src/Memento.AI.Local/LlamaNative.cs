using System.Runtime.InteropServices;
using LLama.Native;

namespace Memento.AI.Local;

/// <summary>
/// Loads llama.cpp once per process: the Vulkan build when a graphics card may be used (it needs only the GPU driver's
/// <c>vulkan-1.dll</c>), the CPU build otherwise, never CUDA, falling back to the CPU build when Vulkan cannot load.
/// The choice is final for the process (one job per worker process). Native log lines go to a small ring buffer for
/// diagnostics; they describe devices and buffers, never prompt text.
/// </summary>
internal static class LlamaNative
{
    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private static string? _backend;

    /// <summary><c>vulkan</c> or <c>cpu</c>, once loaded.</summary>
    public static string? Backend => _backend;

    public static IReadOnlyList<string> RecentLog
    {
        get
        {
            lock (Recent)
            {
                return [.. Recent];
            }
        }
    }

    /// <summary>Loads the native library if needed and returns the backend that is loaded.</summary>
    public static string EnsureLoaded(bool preferVulkan)
    {
        lock (Gate)
        {
            if (_backend is not null)
            {
                return _backend;
            }

            NativeLibraryConfig.All
                .WithCuda(false)
                .WithVulkan(preferVulkan)
                .WithAutoFallback(true)
                .WithLogCallback(OnLog);
            NativeApi.llama_empty_call();
            _backend = IsLoaded("ggml-vulkan.dll") ? "vulkan" : "cpu";
            return _backend;
        }
    }

    public static bool IsLoaded(string module) => ModuleHandle(module) != IntPtr.Zero;

    public static IntPtr ModuleHandle(string module) => GetModuleHandleW(module);

    private static void OnLog(LLamaLogLevel level, string message)
    {
        if (level is LLamaLogLevel.Debug or LLamaLogLevel.Continue || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (Recent)
        {
            Recent.Enqueue("[" + level + "] " + message.TrimEnd());
            while (Recent.Count > 200)
            {
                Recent.Dequeue();
            }
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetModuleHandleW(string moduleName);
}
