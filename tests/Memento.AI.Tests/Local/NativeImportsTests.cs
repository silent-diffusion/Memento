using System.Reflection.PortableExecutable;
using System.Text;
using Xunit.Abstractions;

namespace Memento.AI.Tests.Local;

/// <summary>
/// PE import scan of the llama.cpp DLLs the LLamaSharp backends ship (security audit item 7 and the packaging rule):
/// they may import only Windows system DLLs, the Universal CRT, the Visual C++ runtime the worker already carries
/// app-locally, the graphics driver's <c>vulkan-1.dll</c>, and each other. No CUDA, no surprise dependencies.
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

    [Fact]
    public void EveryLibraryNeedsTheVcRuntimeThatTheWorkerShipsAppLocally()
    {
        var llama = Imports(Path.Combine(Native, "vulkan", "llama.dll"));

        Assert.Contains("MSVCP140.dll", llama, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VCRUNTIME140.dll", llama, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("vulkan-1.dll", Imports(Path.Combine(Native, "vulkan", "ggml-vulkan.dll")), StringComparer.OrdinalIgnoreCase);
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
