using System.Runtime.InteropServices;

namespace Memento.App.Hosting;

/// <summary>
/// Narrows where Windows looks for a DLL that is loaded by name (security audit 2, SA2-09): the application folder,
/// System32 and folders added on purpose, never the current directory or <c>PATH</c>. Media Foundation, Direct3D and the
/// Vortice and SharpGen wrappers import system DLLs by name without a search-path attribute; Memento's own imports are
/// already pinned to System32, and .NET loads the packaged native libraries (WebView2 loader, SQLite) by full path.
/// </summary>
internal static partial class DllSearchPath
{
    private const uint LoadLibrarySearchDefaultDirs = 0x0000_1000;

    /// <summary>Called first thing in <c>Main</c>, before any DLL is loaded by name. Returns <c>false</c> if Windows refused.</summary>
    public static bool Restrict() => SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint directoryFlags);
}
