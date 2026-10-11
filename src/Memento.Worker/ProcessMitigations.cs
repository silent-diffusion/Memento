using System.Runtime.InteropServices;

namespace Memento.Worker;

/// <summary>
/// A one-way limit the worker puts on itself before any engine loads (security audit 2, SA2-22): it may not load a DLL
/// from a network share, whatever an engine or a driver asks for. Forbidding child processes as well was tried and
/// left out: with it the Vulkan loader found no driver and the local model could not use the graphics card.
/// </summary>
internal static partial class ProcessMitigations
{
    private const int ProcessImageLoadPolicy = 10;
    private const uint NoRemoteImages = 0x1;

    /// <summary>Applies the limit; returns <c>false</c> when Windows refused it (it is available from Windows 10).</summary>
    public static bool Apply()
    {
        var images = NoRemoteImages;
        return SetProcessMitigationPolicy(ProcessImageLoadPolicy, ref images, sizeof(uint));
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessMitigationPolicy(int policy, ref uint buffer, nuint length);
}
