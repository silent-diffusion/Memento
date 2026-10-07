namespace Memento.Transcription.Tests;

/// <summary>
/// The worker folder is self-sufficient on a PC without the Visual C++ Redistributable: every native DLL's imports
/// resolve to a file in the worker folder (or the DLL's own folder), a Windows API set, a Windows system DLL, or the
/// GPU driver's Vulkan loader. Whisper.net's foreign runtimes are stripped and no CUDA runtime ships.
/// </summary>
[Trait("Category", "Packaging")]
public sealed class WorkerPackagingTests
{
    /// <summary>DLLs every Windows 10/11 installation has (System32), or the graphics driver installs.</summary>
    private static readonly HashSet<string> System = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel32.dll", "advapi32.dll", "user32.dll", "gdi32.dll", "ole32.dll", "oleaut32.dll", "shell32.dll", "shlwapi.dll",
        "ws2_32.dll", "bcrypt.dll", "crypt32.dll", "ntdll.dll", "rpcrt4.dll", "setupapi.dll", "dbghelp.dll", "dxgi.dll",
        "dxcore.dll", "d3d12.dll", "version.dll", "winmm.dll", "ucrtbase.dll", "secur32.dll", "iphlpapi.dll", "psapi.dll",
        "mfplat.dll", "mfreadwrite.dll", "mf.dll", "propsys.dll", "comctl32.dll", "comdlg32.dll", "winspool.drv", "imm32.dll",
        "vulkan-1.dll", "normaliz.dll", "wldap32.dll", "msvcrt.dll", "combase.dll", "userenv.dll", "powrprof.dll", "dwmapi.dll",
        "uxtheme.dll", "msimg32.dll", "usp10.dll", "api-ms-win-core-path-l1-1-0.dll", "mscoree.dll",
    };

    private static readonly string[] VcRuntime = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll"];

    [Fact]
    public void TheVisualCppRuntimeShipsBesideTheWorker()
    {
        if (WorkerBuild.Directory is not { } folder)
        {
            return; // Not built in this run (the build step produces it).
        }

        Assert.All(VcRuntime, dll => Assert.True(File.Exists(Path.Combine(folder, dll)), $"{dll} is missing from {folder}"));
    }

    [Fact]
    public void EveryNativeImportResolvesWithoutAnInstalledRedistributable()
    {
        if (WorkerBuild.Directory is not { } folder)
        {
            return;
        }

        var unresolved = new List<string>();
        var checkedFiles = 0;
        foreach (var dll in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
        {
            if (PeImports.Read(dll) is not { } imports)
            {
                continue;
            }

            checkedFiles++;
            var own = Path.GetDirectoryName(dll)!;
            foreach (var import in imports)
            {
                var resolved = import.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase)
                    || import.StartsWith("ext-ms-", StringComparison.OrdinalIgnoreCase)
                    || System.Contains(import)
                    || File.Exists(Path.Combine(own, import))
                    || File.Exists(Path.Combine(folder, import));
                if (!resolved)
                {
                    unresolved.Add($"{Path.GetRelativePath(folder, dll)} → {import}");
                }
            }
        }

        Assert.True(checkedFiles >= 6, $"Only {checkedFiles} native DLLs were found in {folder}.");
        Assert.Empty(unresolved);
    }

    [Fact]
    public void OnlyTheWindowsX64VulkanAndCpuRuntimesShip()
    {
        if (WorkerBuild.Directory is not { } folder)
        {
            return;
        }

        var runtimes = Directory.EnumerateDirectories(Path.Combine(folder, "runtimes"), "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(folder, d).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["runtimes/vulkan", "runtimes/vulkan/win-x64", "runtimes/win-x64"], runtimes);
        Assert.DoesNotContain(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories), f => f.Contains("cuda", StringComparison.OrdinalIgnoreCase) || f.Contains("cublas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheImportReaderFindsTheImportsOfAKnownSystemDll()
    {
        var imports = PeImports.Read(Path.Combine(Environment.SystemDirectory, "dxgi.dll"));

        Assert.NotNull(imports);
        Assert.Contains(imports, i => i.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) || i.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase));
    }
}
