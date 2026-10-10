using System.Diagnostics;
using System.Globalization;
using Memento.Audio;
using Memento.Video.Encoding;
using Memento.Video.Gpu;
using Memento.Video.Recording;
using Memento.Video.Recovery;
using Memento.Video.Sources;

namespace Memento.Tools.VideoCheck;

/// <summary>
/// capture  --display 1 --seconds 20 [--adapter nvidia|amd|N] [--no-cursor] [--border] [--window "title part"]
/// record   --display 1 --seconds 20 --out x.mp4 [--size 2560x1440] [--fps 30] [--quality Balanced] [--software] [--adapter ...]
/// synthetic --size 2560x1440 --seconds 20 --out x.mp4 [--fps 30] [--quality ...] [--software]
/// crash    (record options) --after 7.5   — dies without finalizing after that many seconds
/// inspect  --file x.mp4
/// recover  --file x.mp4 [--out y.mp4]
/// </summary>
internal static class Checks
{
    public static long? AdapterLuid(Options o)
    {
        var choice = o.Get("adapter", string.Empty);
        if (choice.Length == 0)
        {
            return null;
        }

        var adapters = VideoDevice.ListAdapters();
        var match = choice switch
        {
            "nvidia" => adapters.FirstOrDefault(a => a.VendorId == GpuAdapterInfo.VendorNvidia),
            "amd" => adapters.FirstOrDefault(a => a.VendorId == GpuAdapterInfo.VendorAmd),
            "intel" => adapters.FirstOrDefault(a => a.VendorId == GpuAdapterInfo.VendorIntel),
            _ => adapters.FirstOrDefault(a => a.Index == int.Parse(choice, CultureInfo.InvariantCulture)),
        };
        return match?.Luid;
    }

    public static IVideoSource OpenSource(VideoDevice device, Options o, long originQpc, int fps, (int W, int H) size, bool synthetic)
    {
        if (synthetic)
        {
            return new SyntheticSource(device, size.W, size.H, originQpc, fps) { Busy = o.Flag("busy") };
        }

        var captureOptions = new GraphicsCaptureOptions(IncludeCursor: !o.Flag("no-cursor"), ShowBorder: o.Flag("border"));
        if (o.Has("window"))
        {
            var part = o.Get("window", string.Empty);
            var window = VideoSourceEnumerator.ListWindows(Environment.ProcessId).First(w => w.Name.Contains(part, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"Window: {window.Name} ({window.Detail})");
            return GraphicsCaptureSource.ForWindow(device, window.Id, window.Name, captureOptions);
        }

        var displays = VideoSourceEnumerator.ListDisplays();
        var display = displays[o.Int("display", 1) - 1];
        Console.WriteLine($"Display: {display.Name} {display.Detail} ({display.Id})");
        return GraphicsCaptureSource.ForDisplay(device, display.Id, display.Name, captureOptions);
    }

    /// <summary>--animate [half|full]: a repainting window on the captured display, so it delivers frames at its refresh rate.</summary>
    public static Animator? StartAnimator(Options o)
    {
        if (!o.Has("animate"))
        {
            return null;
        }

        var display = VideoSourceEnumerator.ListDisplays()[o.Int("display", 1) - 1];
        var monitor = Memento.Video.Interop.User32.Monitors().First(m => m.DeviceName == display.Id.Key);
        var b = monitor.Bounds;
        var (w, h) = o.Get("animate", "half") switch
        {
            "full" => (b.Width, b.Height),
            "small" => (480, 270),
            _ => (b.Width / 2, b.Height / 2),
        };
        return new Animator(new System.Drawing.Rectangle(b.Left, b.Top, w, h));
    }

    private static (int W, int H)? ParseSize(Options o)
    {
        if (!o.Has("size"))
        {
            return null;
        }

        var parts = o.Get("size", "1920x1080").Split('x');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public static async Task<int> Capture(Options o)
    {
        using var device = VideoDevice.Create(AdapterLuid(o));
        Console.WriteLine($"Device: {device.Adapter.Name}");
        var seconds = o.Double("seconds", 10);
        using var animator = StartAnimator(o);
        await using var source = OpenSource(device, o, QpcClock.Now, 30, (0, 0), synthetic: false);
        var cpu = new CpuMeter();
        using var gpu = new GpuEngineSampler(Environment.ProcessId);
        await source.StartAsync(CancellationToken.None);
        if (source is GraphicsCaptureSource g)
        {
            Console.WriteLine($"Border hidden: {g.BorderHidden}");
        }

        var watch = Stopwatch.StartNew();
        var samples = 0;
        long lastFrames = 0;
        var perSecond = new List<long>();
        while (watch.Elapsed.TotalSeconds < seconds && source.State != VideoSourceState.Lost)
        {
            Thread.Sleep(1000);
            gpu.Sample();
            samples++;
            var stats = source.Statistics;
            perSecond.Add(stats.FramesArrived - lastFrames);
            lastFrames = stats.FramesArrived;
        }

        var final = source.Statistics;
        Console.WriteLine($"Animator painted {animator?.Frames ?? 0} frames");
        await source.StopAsync();
        Console.WriteLine($"Frames arrived: {final.FramesArrived} in {watch.Elapsed.TotalSeconds:0.0} s = {final.FramesArrived / watch.Elapsed.TotalSeconds:0.0} fps; per second min {perSecond.DefaultIfEmpty().Min()} max {perSecond.DefaultIfEmpty().Max()}; size {final.Width}x{final.Height}");
        Console.WriteLine($"Arrival − SystemRelativeTime: min {Qpc.Ms(final.ArrivalLatency.MinTicks):0.0} avg {final.ArrivalLatency.AverageTicks / 1e4:0.0} max {Qpc.Ms(final.ArrivalLatency.MaxTicks):0.0} ms; state {source.State}; loss {source.Loss?.Message}");
        Console.WriteLine($"CPU (this process, share of all cores): {cpu.Percent:0.00} %");
        Console.WriteLine(gpu.Report(samples));
        return 0;
    }

    public static async Task<int> Record(Options o, bool synthetic) => await RecordCore(o, synthetic, crashAfterSeconds: null);

    public static async Task<int> Crash(Options o) => await RecordCore(o, o.Flag("synthetic"), o.Double("after", 7.5));

    private static async Task<int> RecordCore(Options o, bool synthetic, double? crashAfterSeconds)
    {
        using var device = VideoDevice.Create(AdapterLuid(o));
        Console.WriteLine($"Device: {device.Adapter.Name}");
        var fps = o.Int("fps", 30);
        var seconds = o.Double("seconds", 10);
        var quality = Enum.Parse<VideoQuality>(o.Get("quality", "Balanced"), ignoreCase: true);
        var origin = QpcClock.Now;
        var size = ParseSize(o);
        using var animator = synthetic ? null : StartAnimator(o);
        await using var source = OpenSource(device, o, origin, fps, size ?? (1920, 1080), synthetic);
        await source.StartAsync(CancellationToken.None);

        // Wait for the first picture so the track size can follow the source.
        var waitStart = Stopwatch.StartNew();
        VideoFrame first;
        while (!source.TryGetLatest(out first) && waitStart.ElapsedMilliseconds < 3000)
        {
            Thread.Sleep(10);
        }

        var (w, h) = size ?? VideoEncoderSettings.EncodableSize(first.Width, first.Height);
        var settings = new VideoEncoderSettings(w, h, fps, quality, PreferHardware: !o.Flag("software"), KeyframeIntervalSeconds: o.Int("gop", 2));
        var outPath = Path.GetFullPath(o.Get("out", "artifacts/video/check.mp4"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        Mp4TrackWriter.MeasureLatencyBySpinning = o.Flag("exact-latency");
        var openWatch = Stopwatch.StartNew();
        await using var writer = Mp4TrackWriter.Create(device, outPath, settings);
        Console.WriteLine($"Encoder: {writer.Encoder.Name} (hardware {writer.Encoder.IsHardware}), opened in {openWatch.ElapsedMilliseconds} ms; {w}x{h} {fps} fps {quality} = {settings.BitrateBitsPerSecond / 1e6:0.0} Mb/s target");

        var cpu = new CpuMeter();
        using var gpu = new GpuEngineSampler(Environment.ProcessId);
        using var waiter = new HighResolutionWaiter();
        using var never = new ManualResetEvent(false);
        origin = QpcClock.Now;
        var pacer = new FramePacer(origin, fps);
        Thread.CurrentThread.Priority = o.Get("priority", "above") == "normal" ? ThreadPriority.Normal : ThreadPriority.AboveNormal;
        var end = origin + (long)(seconds * QpcClock.TicksPerSecond);
        var crashAt = crashAfterSeconds is null ? long.MaxValue : origin + (long)(crashAfterSeconds.Value * QpcClock.TicksPerSecond);
        var nextSample = origin + QpcClock.TicksPerSecond;
        var nextCheckpoint = origin + (5 * QpcClock.TicksPerSecond);
        var samples = 0;
        long maxLate = 0;
        double writeSum = 0;
        double writeMax = 0;
        long writes = 0;
        if (o.Flag("fast") && source is SyntheticSource fastSource)
        {
            return await EncodeFast(o, fastSource, writer, origin, fps, seconds, outPath);
        }

        while (QpcClock.Now < end)
        {
            waiter.WaitUntil(pacer.NextDueQpc, never);
            var now = QpcClock.Now;
            maxLate = Math.Max(maxLate, now - pacer.NextDueQpc);
            var t0 = QpcClock.Now;
            var inGap = o.Has("gap-at") && Qpc.Ms(now - origin) / 1000 is var sec && sec >= o.Double("gap-at", 0) && sec < o.Double("gap-at", 0) + o.Double("gap-length", 3);
            var result = pacer.Tick(now, source, writer, paused: inGap || source.State != VideoSourceState.Running);
            if (result is not null)
            {
                var ms = Qpc.Ms(QpcClock.Now - t0);
                writeSum += ms;
                writeMax = Math.Max(writeMax, ms);
                writes++;
            }

            if (result == VideoWriteResult.Failed)
            {
                Console.WriteLine($"Writer failed: {writer.Statistics.Failure?.Message} | {writer.Statistics.Failure?.InnerException}");
                break;
            }

            if (now >= nextSample)
            {
                gpu.Sample();
                samples++;
                nextSample += QpcClock.TicksPerSecond;
            }

            if (now >= nextCheckpoint)
            {
                writer.Checkpoint();
                nextCheckpoint += 5 * QpcClock.TicksPerSecond;
            }

            if (now >= crashAt)
            {
                Console.WriteLine($"Crashing at {Qpc.Ms(now - origin) / 1000:0.000} s after {pacer.FramesWritten} frames, file {new FileInfo(outPath).Length} bytes");
                Console.Out.Flush();
                Environment.FailFast("VideoCheck crash test: the process dies without finalizing the video file.");
            }
        }

        var elapsed = Qpc.Ms(QpcClock.Now - origin) / 1000;
        var cpuPercent = cpu.Percent;
        await writer.FinalizeAsync(CancellationToken.None);
        await source.StopAsync();
        var stats = writer.Statistics;
        var sourceStats = source.Statistics;
        Console.WriteLine($"Ran {elapsed:0.0} s: written {pacer.FramesWritten}, dropped {pacer.FramesDropped}, idle {pacer.FramesIdle}; source delivered {sourceStats.FramesArrived} pictures ({sourceStats.FramesArrived / elapsed:0.0}/s)");
        Console.WriteLine($"Pacing: max wake-up lateness {Qpc.Ms(maxLate):0.00} ms; WriteFrame avg {writeSum / Math.Max(1, writes):0.00} ms max {writeMax:0.00} ms");
        Console.WriteLine($"Encoder latency (submit → encoded): avg {stats.AverageLatencyMs:0.0} ms, max {stats.MaxLatencyMs:0.0} ms; encoded {stats.FramesEncoded}; finalize {writer.FinalizeMilliseconds:0} ms");
        Console.WriteLine($"File: {stats.BytesWritten} bytes = {stats.BytesWritten * 8 / elapsed / 1e6:0.00} Mb/s actual = {VideoBitrate.BytesPerHour((int)(stats.BytesWritten * 8 / elapsed)) / 1e9:0.00} GB/hour");
        Console.WriteLine($"CPU (this process, share of all cores): {cpuPercent:0.00} %");
        Console.WriteLine(gpu.Report(samples));
        PrintSummary(outPath);
        return 0;
    }

    /// <summary>--fast: encodes synthetic slots back to back on a virtual clock, to make hours-long test files in minutes.</summary>
    private static async Task<int> EncodeFast(Options o, SyntheticSource source, Mp4TrackWriter writer, long origin, int fps, double seconds, string outPath)
    {
        var period = 10_000_000L / fps;
        var slots = (long)(seconds * fps);
        long virtualNow = origin;
        source.Clock = () => virtualNow;
        var watch = Stopwatch.StartNew();
        long retries = 0;
        for (long slot = 0; slot < slots; slot++)
        {
            virtualNow = source.OriginQpc + (slot * period);
            source.TryGetLatest(out var frame);
            VideoWriteResult result;
            while ((result = writer.WriteFrame(frame, slot * period)) == VideoWriteResult.NoFreeSurface)
            {
                retries++;
                Thread.Sleep(1);
            }

            if (result == VideoWriteResult.Failed)
            {
                Console.WriteLine($"Writer failed at slot {slot}: {writer.Statistics.Failure?.Message}");
                break;
            }

            if (o.Has("crash-at-slot") && slot == o.Int("crash-at-slot", 0))
            {
                Console.WriteLine($"Crashing at slot {slot}, file {new FileInfo(outPath).Length} bytes");
                Console.Out.Flush();
                Environment.FailFast("VideoCheck crash test (fast mode).");
            }

            if (slot % (fps * 600) == 0 && slot > 0)
            {
                Console.WriteLine($"  {slot / fps / 60} min encoded in {watch.Elapsed.TotalSeconds:0} s");
            }
        }

        await writer.FinalizeAsync(CancellationToken.None);
        Console.WriteLine($"Fast: {slots} frames ({seconds / 60:0.0} min) in {watch.Elapsed.TotalSeconds:0.0} s ({slots / watch.Elapsed.TotalSeconds:0} fps), waits for a free surface {retries}; finalize {writer.FinalizeMilliseconds:0} ms; {writer.Statistics.BytesWritten} bytes");
        PrintSummary(outPath);
        return 0;
    }

    public static int Inspect(Options o)
    {
        PrintSummary(o.Get("file", string.Empty));
        return 0;
    }

    public static void PrintSummary(string path)
    {
        var summary = Mp4FileSummary.Read(path);
        Console.WriteLine($"MP4: fragmented {summary.IsFragmented}, moov {summary.HasMoov}, mfra {summary.HasIndex}, timescale {summary.Timescale}, fragments {summary.Fragments.Count}, samples {summary.Samples}, duration {summary.DurationSeconds:0.000} s, valid {summary.ValidLength} bytes, torn {summary.TornBytes} bytes");
        if (summary.Fragments.Count > 0 && summary.Timescale > 0)
        {
            var first = summary.Fragments[0];
            var lengths = summary.Fragments.Select(f => f.Duration / (double)summary.Timescale).ToList();
            Console.WriteLine($"First fragment decode time {first.DecodeTime / (double)summary.Timescale:0.000} s; fragment length min {lengths.Min():0.000} s max {lengths.Max():0.000} s; samples/fragment {summary.Fragments.Average(f => f.Samples):0.0}");
        }
    }

    public static async Task<int> Recover(Options o)
    {
        var file = o.Get("file", string.Empty);
        var output = o.Get("out", Path.ChangeExtension(file, ".recovered.mp4"));
        var watch = Stopwatch.StartNew();
        var result = await VideoRecovery.RecoverAsync(file, output, CancellationToken.None);
        Console.WriteLine($"Recovered in {watch.ElapsedMilliseconds} ms: {result}");
        PrintSummary(output);
        return 0;
    }
}
