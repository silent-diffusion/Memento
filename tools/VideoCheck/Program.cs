using System.Diagnostics;
using System.Globalization;
using Memento.Audio;
using Memento.Video.Encoding;
using Memento.Video.Gpu;
using Memento.Video.Interop;
using Memento.Video.Recording;
using Memento.Video.Sources;

namespace Memento.Tools.VideoCheck;

/// <summary>
/// The video spike's measurements (ENGINE-NOTES.md §M). Every command prints plain numbers. Files are written only
/// where --out says (use the worktree's artifacts folder); nothing is uploaded.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("VideoCheck probe | capture | record | synthetic | sync | crash | recover | inspect   (see Checks.cs for options)");
            return 1;
        }

        var options = Options.Parse(args.Skip(1));
        try
        {
            return args[0] switch
            {
                "probe" => Probe(),
                "capture" => Checks.Capture(options).GetAwaiter().GetResult(),
                "record" => Checks.Record(options, synthetic: false).GetAwaiter().GetResult(),
                "synthetic" => Checks.Record(options, synthetic: true).GetAwaiter().GetResult(),
                "sync" => SyncCheck.Run(options),
                "crash" => Checks.Crash(options).GetAwaiter().GetResult(),
                "recover" => Checks.Recover(options).GetAwaiter().GetResult(),
                "inspect" => Checks.Inspect(options),
                _ => 1,
            };
        }
        catch (Memento.Video.VideoCaptureException ex)
        {
            Console.WriteLine($"VideoCaptureException {ex.Code}: {ex.Message} (0x{ex.HResultCode ?? 0:X8})");
            return 2;
        }
    }

    private static int Probe()
    {
        Console.WriteLine($"Windows {Environment.OSVersion.Version}; GraphicsCaptureSession.IsSupported = {GraphicsCaptureSource.IsSupported()}");
        foreach (var adapter in VideoDevice.ListAdapters())
        {
            var outputs = VideoDevice.DisplaysByAdapter().GetValueOrDefault(adapter.Luid) ?? [];
            Console.WriteLine($"Adapter {adapter.Index}: {adapter.Name} vendor 0x{adapter.VendorId:X4} luid 0x{adapter.Luid:X} {adapter.DedicatedMemoryBytes / 1048576} MB; drives [{string.Join(", ", outputs)}]; encoder {H264EncoderProbe.HardwareFor(adapter)?.Name ?? "none"}");
        }

        foreach (var encoder in H264EncoderProbe.List())
        {
            Console.WriteLine($"H.264 encoder: {encoder.Name} hardware={encoder.IsHardware} luid=0x{encoder.AdapterLuid ?? 0:X} vendor={encoder.VendorId}");
        }

        foreach (var monitor in User32.Monitors())
        {
            Console.WriteLine($"Monitor {monitor.DeviceName} {monitor.Bounds.Width}x{monitor.Bounds.Height} at ({monitor.Bounds.Left},{monitor.Bounds.Top}) primary={monitor.IsPrimary} dpi={monitor.DpiX}");
        }

        foreach (var source in VideoSourceEnumerator.ListDisplays())
        {
            Console.WriteLine($"Display source: {source.Name} | {source.Detail} | {source.Id}");
        }

        foreach (var source in VideoSourceEnumerator.ListWindows(Environment.ProcessId).Take(15))
        {
            Console.WriteLine($"Window source: {source.Name} | {source.Detail} | excluded={source.IsExcluded}");
        }

        foreach (var camera in VideoSourceEnumerator.ListCameras())
        {
            Console.WriteLine($"Camera: {camera.Name} | {camera.Detail} | {camera.Id}");
        }

        Console.WriteLine($"Camera privacy (this user, desktop apps): {CameraPrivacy.Read()}");
        return 0;
    }
}

/// <summary>--name value pairs.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Options Parse(IEnumerable<string> args)
    {
        var options = new Options();
        string? key = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (key is not null)
                {
                    options._values[key] = "true";
                }

                key = arg[2..];
            }
            else if (key is not null)
            {
                options._values[key] = arg;
                key = null;
            }
        }

        if (key is not null)
        {
            options._values[key] = "true";
        }

        return options;
    }

    public string Get(string name, string fallback) => _values.TryGetValue(name, out var value) ? value : fallback;

    public int Int(string name, int fallback) => _values.TryGetValue(name, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    public double Double(string name, double fallback) => _values.TryGetValue(name, out var value) ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;

    public bool Flag(string name) => _values.TryGetValue(name, out var value) && value != "false";

    public bool Has(string name) => _values.ContainsKey(name);
}

/// <summary>Process CPU time over wall time, as a share of the whole processor (all cores = 100 %).</summary>
internal sealed class CpuMeter
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly TimeSpan _start;

    public CpuMeter()
    {
        _start = _process.TotalProcessorTime;
    }

    public double Percent
    {
        get
        {
            _process.Refresh();
            return (_process.TotalProcessorTime - _start).TotalSeconds / _wall.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
        }
    }
}

internal static class Qpc
{
    public static double Ms(long ticks) => ticks / (double)QpcClock.TicksPerMillisecond;
}
