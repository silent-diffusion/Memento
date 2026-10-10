using System.Globalization;
using System.Runtime.InteropServices;

namespace Memento.Tools.VideoCheck;

/// <summary>
/// Samples <c>\GPU Engine(*)\Utilization Percentage</c> through PDH once a second and sums it per engine type
/// (3D, VideoEncode, VideoDecode, Copy, VideoProcessing) for one process and for the whole PC.
/// </summary>
internal sealed class GpuEngineSampler : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private readonly IntPtr _query;
    private readonly IntPtr _counter;
    private readonly int _pid;
    private readonly Dictionary<string, (double Sum, int Count, double Max)> _process = [];
    private readonly Dictionary<string, (double Sum, int Count, double Max)> _all = [];

    public GpuEngineSampler(int pid)
    {
        _pid = pid;
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) == 0)
        {
            _ = PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _counter);
            _ = PdhCollectQueryData(_query);
        }
    }

    public void Sample()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
        {
            return;
        }

        uint size = 0;
        if (PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out var count, IntPtr.Zero) != PdhMoreData)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out count, buffer) != 0)
            {
                return;
            }

            var mine = new Dictionary<string, double>();
            var all = new Dictionary<string, double>();
            var itemSize = IntPtr.Size + 16;
            for (var i = 0; i < count; i++)
            {
                var item = buffer + (i * itemSize);
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? string.Empty;
                var status = Marshal.ReadInt32(item + IntPtr.Size);
                if (status is not (0 or 1))
                {
                    continue;
                }

                var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                var type = name[(name.LastIndexOf("engtype_", StringComparison.Ordinal) + 8)..];
                var luid = name.Contains("luid_", StringComparison.Ordinal) ? name.Substring(name.IndexOf("luid_", StringComparison.Ordinal), 26) : "?";
                var key = type + "@" + luid;
                all[key] = all.GetValueOrDefault(key) + value;
                if (name.StartsWith(string.Create(CultureInfo.InvariantCulture, $"pid_{_pid}_"), StringComparison.Ordinal))
                {
                    mine[key] = mine.GetValueOrDefault(key) + value;
                }
            }

            Add(_process, mine);
            Add(_all, all);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Add(Dictionary<string, (double Sum, int Count, double Max)> into, Dictionary<string, double> sample)
    {
        foreach (var (key, value) in sample)
        {
            var current = into.GetValueOrDefault(key);
            into[key] = (current.Sum + value, current.Count + 1, Math.Max(current.Max, value));
        }
    }

    public string Report(int samples)
    {
        static string Line(Dictionary<string, (double Sum, int Count, double Max)> data, int samples) =>
            string.Join("; ", data.Where(kv => kv.Value.Max >= 0.5).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} avg {kv.Value.Sum / Math.Max(1, samples):0.0}% max {kv.Value.Max:0.0}%")));
        return "GPU (this process): " + Line(_process, samples) + "\nGPU (whole PC):     " + Line(_all, samples);
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            _ = PdhCloseQuery(_query);
        }
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
