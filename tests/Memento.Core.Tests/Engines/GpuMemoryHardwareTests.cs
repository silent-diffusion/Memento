using System.Diagnostics;
using System.Globalization;
using Memento.Core.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Memento.Core.Tests.Engines;

/// <summary>
/// The probe against the NVIDIA driver's own numbers on a real card (ENGINE-NOTES.md §J). Run with
/// <c>dotnet test --filter Category=Hardware</c> on a PC with an NVIDIA card; skipped where <c>nvidia-smi</c> is missing.
/// The card may be shared with other programs while it runs: both readings are taken around each sample.
/// </summary>
public sealed class GpuMemoryHardwareTests(ITestOutputHelper output)
{
    /// <summary>The two readings may differ by what changed between them and the driver's own reservations.</summary>
    private const long Tolerance = 300L * 1024 * 1024;

    private const long Mib = 1024L * 1024;

    [Trait("Category", "Hardware")]
    [NvidiaSmiFact]
    public void FreeAndUsedMemoryAgreeWithNvidiaSmi()
    {
        var probe = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance);

        var before = NvidiaSmi.Read();
        var gpu = probe.Refresh().DiscreteGpu;
        var after = NvidiaSmi.Read();

        Assert.NotNull(gpu);
        Assert.Contains("NVIDIA", gpu.Name, StringComparison.Ordinal);
        output.WriteLine($"nvidia-smi free {before.Free / Mib}–{after.Free / Mib} MiB, used {before.Used / Mib}–{after.Used / Mib} MiB of {before.Total / Mib} MiB");
        output.WriteLine($"probe free {gpu.FreeVramBytes / Mib} MiB, used {gpu.UsedBytes / Mib} MiB of {gpu.DedicatedVideoMemoryBytes / Mib} MiB");
        foreach (var holder in gpu.Holders)
        {
            output.WriteLine($"  {holder.Bytes / Mib} MiB {holder.Description}");
        }

        output.WriteLine(GpuMemoryWording.Summary(gpu));
        Assert.InRange(gpu.FreeVramBytes!.Value, Math.Min(before.Free, after.Free) - Tolerance, Math.Max(before.Free, after.Free) + Tolerance);
        Assert.InRange(gpu.UsedBytes!.Value, Math.Min(before.Used, after.Used) - Tolerance, Math.Max(before.Used, after.Used) + Tolerance);
    }

    [Trait("Category", "Hardware")]
    [NvidiaSmiFact]
    public void TheHoldersAreOnTheCardAndAddUpToNoMoreThanItUses()
    {
        var gpu = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance).Refresh().DiscreteGpu;

        Assert.NotNull(gpu);
        Assert.True(gpu.Holders.Count <= GpuMemoryAttribution.MaxHolders);
        Assert.Equal(gpu.Holders.OrderByDescending(h => h.Bytes).Select(h => h.Bytes), gpu.Holders.Select(h => h.Bytes));
        Assert.All(gpu.Holders, h => Assert.True(h.Bytes >= GpuMemoryAttribution.MinimumHolderBytes));
        Assert.True(gpu.Holders.Sum(h => h.Bytes) <= gpu.UsedBytes!.Value + Tolerance, "the holders hold more than the whole card uses");
    }

    [Trait("Category", "Hardware")]
    [NvidiaSmiFact]
    public void TheIntegratedGpuIsNeverTakenForTheCard()
    {
        var snapshot = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance).Refresh();

        foreach (var gpu in snapshot.Gpus)
        {
            output.WriteLine($"{gpu.Name}: vendor 0x{gpu.VendorId:X4}, {gpu.DedicatedVideoMemoryBytes / Mib} MiB, discrete {gpu.IsDiscrete}, {gpu.Holders.Count} holders");
        }

        Assert.Equal(0x10DE, snapshot.DiscreteGpu!.VendorId);
        Assert.All(snapshot.Gpus.Where(g => g.VendorId != 0x10DE), g => Assert.False(g.IsDiscrete, $"{g.Name} was taken for a separate card"));
        Assert.All(snapshot.Gpus.Where(g => !g.IsDiscrete), g => Assert.Empty(g.Holders));
    }

    [Trait("Category", "Hardware")]
    [NvidiaSmiFact]
    public void AReadingIsCheapOnceWarm()
    {
        var probe = new WindowsResourceProbe(NullLogger<WindowsResourceProbe>.Instance);
        probe.Refresh();

        var watch = Stopwatch.StartNew();
        probe.Refresh();
        var refresh = watch.Elapsed;
        watch.Restart();
        probe.Sample();
        var cached = watch.Elapsed;

        output.WriteLine($"refresh {refresh.TotalMilliseconds:0.0} ms, cached sample {cached.TotalMilliseconds:0.0} ms");
        Assert.True(refresh < TimeSpan.FromMilliseconds(500), $"a refresh took {refresh.TotalMilliseconds:0} ms");
        Assert.True(cached < refresh + TimeSpan.FromMilliseconds(50));
    }

    /// <summary>A hardware fact that needs the NVIDIA driver's <c>nvidia-smi</c>.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    private sealed class NvidiaSmiFactAttribute : FactAttribute
    {
        public NvidiaSmiFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || NvidiaSmi.Path is null)
            {
                Skip = "nvidia-smi was not found (no NVIDIA card or driver).";
            }
        }
    }

    private static class NvidiaSmi
    {
        public static string? Path { get; } = Find();

        public static (long Total, long Used, long Free) Read()
        {
            using var process = Process.Start(new ProcessStartInfo(Path!, "--query-gpu=memory.total,memory.used,memory.free --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            var line = process.StandardOutput.ReadLine() ?? throw new InvalidOperationException("nvidia-smi printed nothing.");
            process.WaitForExit(10_000);
            var values = line.Split(',').Select(v => long.Parse(v.Trim(), CultureInfo.InvariantCulture) * Mib).ToArray();
            return (values[0], values[1], values[2]);
        }

        private static string? Find()
        {
            var system = System.IO.Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            return File.Exists(system) ? system : null;
        }
    }
}
