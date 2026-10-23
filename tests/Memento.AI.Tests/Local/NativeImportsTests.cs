using System.Reflection.PortableExecutable;
using System.Text;
using Xunit.Abstractions;

namespace Memento.AI.Tests.Local;

/// <summary>
/// PE import scan of the llama.cpp DLLs the LLamaSharp backends ship (security audit item 7 and the packaging rule):
/// they may import only Windows system DLLs, the Universal CRT, the Visual C++ runtime the worker already carries
/// app-locally, the graphics driver's <c>vulkan-1.dll</c>, and each other. No CUDA, no surprise dependencies. Since
/// 2.4.0 the worker's <c>x64\</c> folder (Tesseract and Leptonica) is scanned too (security audit 2, SA2-23).
/// </summary>
public sealed class NativeImportsTests(ITestOutputHelper output)
{
    private static readonly string Native = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "KERNEL32.dll", "ADVAPI32.dll", "USER32.dll", "ole32.dll", "SHELL32.dll", "WS2_32.dll",
        "MSVCP140.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll", "VCOMP140.DLL",
        "vulkan-1.dll",
        "ggml.dll", "ggml-base.dll", "ggml-cpu.dll", "ggml-vulkan.dll", "llama.dll",
    };

    [Fact]
    public void TheShippedNativeLibrariesImportOnlyTheVcRuntimeTheSystemAndEachOther()
    {
        var files = Directory.GetFiles(Native, "*.dll", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.EndsWith(Path.Combine("vulkan", "ggml-vulkan.dll"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.Contains("cuda", StringComparison.OrdinalIgnoreCase));

        var unexpected = new List<string>();
        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var imports = Imports(file);
            output.WriteLine($"{Path.GetRelativePath(Native, file)} ({new FileInfo(file).Length / 1024} KB): {string.Join(", ", imports)}");
            unexpected.AddRange(imports
                .Where(i => !Allowed.Contains(i) && !i.StartsWith("api-ms-win-crt-", StringComparison.OrdinalIgnoreCase) && !i.Equals("mtmd.dll", StringComparison.OrdinalIgnoreCase))
                .Select(i => Path.GetRelativePath(Native, file) + " → " + i));
        }

        Assert.Empty(unexpected);
    }

    /// <summary>
    /// The worker's <c>x64\</c> folder: Tesseract and Leptonica from the TesseractOCR package (2.0). They may import only
    /// Windows system DLLs, the Universal CRT, the Visual C++ runtime and each other, with one accepted finding:
    /// <c>tesseract55.dll</c> imports <c>WS2_32</c> for its ScrollView debug viewer, a socket client to localhost reached
    /// only through Tesseract parameters Memento never sets and only hash-pinned language data could set (security
    /// audit 2, SA2-23; THIRD-PARTY.md). The test states that exception instead of allowing networking for every DLL,
    /// and fails if the import goes away, so the documents can say so.
    /// </summary>
    [Fact]
    public void TheWorkersTesseractLibrariesImportOnlyTheVcRuntimeTheSystemAndEachOther()
    {
        var folder = WorkerX64Folder();
        var files = Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories);
        Assert.Contains(files, f => Path.GetFileName(f).Equals("tesseract55.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => Path.GetFileName(f).StartsWith("leptonica-", StringComparison.OrdinalIgnoreCase));

        var unexpected = new List<string>();
        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var imports = Imports(file);
            output.WriteLine($"x64\\{Path.GetRelativePath(folder, file)} ({new FileInfo(file).Length / 1024} KB): {string.Join(", ", imports)}");
            unexpected.AddRange(imports
                .Where(i => !WorkerX64Allowed.Contains(i)
                    && !i.StartsWith("api-ms-win-crt-", StringComparison.OrdinalIgnoreCase)
                    && !(i.StartsWith("leptonica-", StringComparison.OrdinalIgnoreCase) && i.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    && !IsAcceptedFinding(name, i))
                .Select(i => name + " → " + i));
        }

        Assert.Empty(unexpected);
        Assert.Contains("WS2_32.dll", Imports(Path.Combine(folder, "tesseract55.dll")), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryLibraryNeedsTheVcRuntimeThatTheWorkerShipsAppLocally()
    {
        var llama = Imports(Path.Combine(Native, "vulkan", "llama.dll"));

        Assert.Contains("MSVCP140.dll", llama, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VCRUNTIME140.dll", llama, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("vulkan-1.dll", Imports(Path.Combine(Native, "vulkan", "ggml-vulkan.dll")), StringComparer.OrdinalIgnoreCase);
    }

    // Networking is deliberately absent: the one WS2_32 import is the accepted finding below, for one DLL only.
    private static readonly HashSet<string> WorkerX64Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "KERNEL32.dll", "USER32.dll", "GDI32.dll", "ADVAPI32.dll",
        "MSVCP140.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll", "VCOMP140.DLL",
    };

    /// <summary>SA2-23, accepted: Tesseract's ScrollView debug client (THIRD-PARTY.md, Tesseract 5.5).</summary>
    private static bool IsAcceptedFinding(string library, string import) =>
        library.Equals("tesseract55.dll", StringComparison.OrdinalIgnoreCase) && import.Equals("WS2_32.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The worker's build output for this test run's configuration (the project reference in the test project builds it
    /// first): <c>src\Memento.Worker\bin\&lt;configuration&gt;\net8.0-windows\win-x64\x64</c>.
    /// </summary>
    private static string WorkerX64Folder()
    {
        var parts = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var bin = Array.FindLastIndex(parts, p => p.Equals("bin", StringComparison.OrdinalIgnoreCase));
        var configuration = bin >= 0 && bin + 1 < parts.Length ? parts[bin + 1] : "Debug";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Memento.sln")))
            {
                var folder = Path.Combine(directory.FullName, "src", "Memento.Worker", "bin", configuration, "net8.0-windows", "win-x64", "x64");
                Assert.True(Directory.Exists(folder), $"The worker's x64 folder is missing at {folder}; build Memento.Worker ({configuration}) first.");
                return folder;
            }
        }

        throw new DirectoryNotFoundException($"Memento.sln was not found above {AppContext.BaseDirectory}.");
    }

    private static List<string> Imports(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var directory = pe.PEHeaders.PEHeader!.ImportTableDirectory;
        var names = new List<string>();
        if (directory.Size == 0)
        {
            return names;
        }

        var image = pe.GetEntireImage().GetContent();
        int Offset(int rva)
        {
            foreach (var section in pe.PEHeaders.SectionHeaders)
            {
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
                {
                    return rva - section.VirtualAddress + section.PointerToRawData;
                }
            }

            return -1;
        }

        for (var descriptor = Offset(directory.RelativeVirtualAddress); descriptor >= 0; descriptor += 20)
        {
            var nameRva = BitConverter.ToInt32(image.AsSpan(descriptor + 12, 4));
            if (nameRva == 0)
            {
                break;
            }

            var at = Offset(nameRva);
            var end = image.IndexOf((byte)0, at);
            names.Add(Encoding.ASCII.GetString(image.AsSpan(at, end - at)));
        }

        return names;
    }
}
